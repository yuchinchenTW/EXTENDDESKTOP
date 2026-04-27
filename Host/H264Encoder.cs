using System;
using System.Runtime.InteropServices;
using ExtentDesktop.Shared;

namespace ExtentDesktop.Host
{
    internal sealed class H264Encoder : IDisposable
    {
        private readonly int _width;
        private readonly int _height;
        private readonly int _fps;
        private readonly long _frameDurationTicks;

        private IMFTransform _encoder;
        private IMFSample _outputSample;
        private IMFMediaBuffer _outputBuffer;
        private IMFMediaBuffer _inputBuffer;
        private int _inputBufferCapacity;
        private bool _encoderProvidesOutputSamples;
        private long _frameIndex;
        private bool _streaming;

        public H264Encoder(int width, int height, int fps, int bitrate)
        {
            if ((width & 1) != 0 || (height & 1) != 0)
            {
                throw new ArgumentException("Width and height must be even for NV12.");
            }

            _width = width;
            _height = height;
            _fps = fps;
            _frameDurationTicks = 10000000L / fps;

            MFHelpers.Check(MFNative.MFStartup(MFConstants.MF_VERSION, MFConstants.MFSTARTUP_LITE), "MFStartup");

            try
            {
                CreateEncoder(bitrate);
                ConfigureCodecApi();
                AllocateBuffers();
                StartStreaming();
            }
            catch
            {
                Cleanup();
                MFNative.MFShutdown();
                throw;
            }
        }

        public void Submit(IntPtr bgraData, int bgraStride)
        {
            IMFSample inputSample;
            FillInputSample(bgraData, bgraStride, out inputSample);
            try
            {
                int hr = _encoder.ProcessInput(0, inputSample, 0);
                MFHelpers.Check(hr, "ProcessInput");
            }
            finally
            {
                Marshal.ReleaseComObject(inputSample);
            }
        }

        public bool TryDrainOutput(out byte[] buffer, out int length, out bool isKeyframe)
        {
            return DrainOutput(out buffer, out length, out isKeyframe);
        }

        public void Dispose()
        {
            try
            {
                if (_streaming && _encoder != null)
                {
                    _encoder.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_END_OF_STREAM, IntPtr.Zero);
                    _encoder.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_END_STREAMING, IntPtr.Zero);
                }
            }
            catch
            {
            }

            Cleanup();

            try
            {
                MFNative.MFShutdown();
            }
            catch
            {
            }
        }

        private void Cleanup()
        {
            if (_outputBuffer != null)
            {
                Marshal.ReleaseComObject(_outputBuffer);
                _outputBuffer = null;
            }
            if (_outputSample != null)
            {
                Marshal.ReleaseComObject(_outputSample);
                _outputSample = null;
            }
            if (_inputBuffer != null)
            {
                Marshal.ReleaseComObject(_inputBuffer);
                _inputBuffer = null;
            }
            if (_encoder != null)
            {
                Marshal.ReleaseComObject(_encoder);
                _encoder = null;
            }
            _streaming = false;
        }

        private void CreateEncoder(int bitrate)
        {
            var clsid = MFGuids.CLSID_CMSH264EncoderMFT;
            var iid = new Guid("bf94c121-5b05-4e6f-8000-ba598961414d");
            object encoderObj;
            MFHelpers.Check(MFNative.CoCreateInstance(ref clsid, IntPtr.Zero, MFConstants.CLSCTX_INPROC_SERVER, ref iid, out encoderObj), "CoCreateInstance(H264 Encoder)");
            _encoder = (IMFTransform)encoderObj;

            SetOutputTypeWithFallbacks(bitrate);

            var inputType = MFHelpers.CreateVideoType(MFGuids.MFVideoFormat_NV12, _width, _height, _fps, 1);
            try
            {
                MFHelpers.Check(_encoder.SetInputType(0, inputType, 0), "SetInputType(NV12)");
            }
            finally
            {
                Marshal.ReleaseComObject(inputType);
            }

            try
            {
                IMFAttributes attrs;
                if (_encoder.GetAttributes(out attrs) == 0 && attrs != null)
                {
                    try
                    {
                        var lowLatencyAttr = MFGuids.MF_LOW_LATENCY;
                        attrs.SetUINT32(ref lowLatencyAttr, 1);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(attrs);
                    }
                }
            }
            catch
            {
            }
        }

        private void SetOutputTypeWithFallbacks(int bitrate)
        {
            int hr = TrySetOutputType(bitrate, profile: 0, level: 0);
            if (hr >= 0) return;

            hr = TrySetOutputType(bitrate, profile: MFConstants.eAVEncH264VProfile_Main, level: 0);
            if (hr >= 0) return;

            hr = TrySetOutputType(bitrate, profile: MFConstants.eAVEncH264VProfile_Base, level: 42);
            if (hr >= 0) return;

            hr = TrySetOutputType(bitrate / 2, profile: 0, level: 0);
            if (hr >= 0) return;

            MFHelpers.Check(hr, "SetOutputType(H264) [all fallbacks]");
        }

        private int TrySetOutputType(int bitrate, int profile, int level)
        {
            var outputType = MFHelpers.CreateVideoType(MFGuids.MFVideoFormat_H264, _width, _height, _fps, 1);
            try
            {
                var bitrateKey = MFGuids.MF_MT_AVG_BITRATE;
                int hr = outputType.SetUINT32(ref bitrateKey, (uint)bitrate);
                if (hr < 0) return hr;

                if (profile != 0)
                {
                    var profileKey = MFGuids.MF_MT_MPEG2_PROFILE;
                    hr = outputType.SetUINT32(ref profileKey, (uint)profile);
                    if (hr < 0) return hr;
                }

                if (level != 0)
                {
                    var levelKey = MFGuids.MF_MT_MPEG2_LEVEL;
                    hr = outputType.SetUINT32(ref levelKey, (uint)level);
                    if (hr < 0) return hr;
                }

                return _encoder.SetOutputType(0, outputType, 0);
            }
            finally
            {
                Marshal.ReleaseComObject(outputType);
            }
        }

        private void ConfigureCodecApi()
        {
            try
            {
                var codecApi = _encoder as ICodecAPI;
                if (codecApi == null)
                {
                    return;
                }

                var lowLatencyKey = MFGuids.CODECAPI_AVLowLatencyMode;
                var rateModeKey = MFGuids.CODECAPI_AVEncCommonRateControlMode;
                var gopKey = MFGuids.CODECAPI_AVEncMPVGOPSize;

                SetVariantBool(codecApi, ref lowLatencyKey, true);
                SetVariantUInt32(codecApi, ref rateModeKey, (uint)MFConstants.eAVEncCommonRateControlMode_CBR);
                SetVariantUInt32(codecApi, ref gopKey, (uint)(_fps * 2));
            }
            catch
            {
            }
        }

        private static void SetVariantBool(ICodecAPI api, ref Guid key, bool value)
        {
            var pv = new PROPVARIANT_BOOL();
            pv.vt = (ushort)VarEnum.VT_BOOL;
            pv.boolVal = value ? unchecked((short)0xFFFF) : (short)0;
            int size = Marshal.SizeOf(typeof(PROPVARIANT_BOOL));
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(pv, buf, false);
                api.SetValue(ref key, buf);
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        private static void SetVariantUInt32(ICodecAPI api, ref Guid key, uint value)
        {
            var pv = new PROPVARIANT_UI4();
            pv.vt = (ushort)VarEnum.VT_UI4;
            pv.ulVal = value;
            int size = Marshal.SizeOf(typeof(PROPVARIANT_UI4));
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(pv, buf, false);
                api.SetValue(ref key, buf);
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        private void AllocateBuffers()
        {
            int nv12Size = PixelConvert.Nv12Size(_width, _height);
            _inputBufferCapacity = nv12Size;
            MFHelpers.Check(MFNative.MFCreateMemoryBuffer(nv12Size, out _inputBuffer), "MFCreateMemoryBuffer(input)");

            MFT_OUTPUT_STREAM_INFO info;
            MFHelpers.Check(_encoder.GetOutputStreamInfo(0, out info), "GetOutputStreamInfo");
            _encoderProvidesOutputSamples = (info.dwFlags & (MFConstants.MFT_OUTPUT_STREAM_PROVIDES_SAMPLES | MFConstants.MFT_OUTPUT_STREAM_CAN_PROVIDE_SAMPLES)) != 0;

            if (!_encoderProvidesOutputSamples)
            {
                int outBufferSize = info.cbSize > 0 ? info.cbSize : (_width * _height * 3 / 2);
                if (outBufferSize < 1024) outBufferSize = 1024 * 1024;

                int alignment = info.cbAlignment > 0 ? info.cbAlignment : 0;
                if (alignment > 1)
                {
                    MFHelpers.Check(MFNative.MFCreateAlignedMemoryBuffer(outBufferSize, alignment - 1, out _outputBuffer), "MFCreateAlignedMemoryBuffer(output)");
                }
                else
                {
                    MFHelpers.Check(MFNative.MFCreateMemoryBuffer(outBufferSize, out _outputBuffer), "MFCreateMemoryBuffer(output)");
                }

                MFHelpers.Check(MFNative.MFCreateSample(out _outputSample), "MFCreateSample(output)");
                MFHelpers.Check(_outputSample.AddBuffer(_outputBuffer), "Output AddBuffer");
            }
        }

        private void StartStreaming()
        {
            MFHelpers.Check(_encoder.ProcessMessage(MFConstants.MFT_MESSAGE_COMMAND_FLUSH, IntPtr.Zero), "FLUSH");
            MFHelpers.Check(_encoder.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, IntPtr.Zero), "BEGIN_STREAMING");
            MFHelpers.Check(_encoder.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_START_OF_STREAM, IntPtr.Zero), "START_OF_STREAM");
            _streaming = true;
        }

        private void FillInputSample(IntPtr bgraData, int bgraStride, out IMFSample sample)
        {
            IntPtr p;
            int maxLen, curLen;
            MFHelpers.Check(_inputBuffer.Lock(out p, out maxLen, out curLen), "InputBuffer.Lock");
            try
            {
                PixelConvert.Bgra32ToNv12(bgraData, bgraStride, p, _width, _height);
            }
            finally
            {
                MFHelpers.Check(_inputBuffer.Unlock(), "InputBuffer.Unlock");
            }
            MFHelpers.Check(_inputBuffer.SetCurrentLength(_inputBufferCapacity), "InputBuffer.SetCurrentLength");

            MFHelpers.Check(MFNative.MFCreateSample(out sample), "MFCreateSample(input)");
            MFHelpers.Check(sample.AddBuffer(_inputBuffer), "Input AddBuffer");

            long pts = _frameIndex * _frameDurationTicks;
            MFHelpers.Check(sample.SetSampleTime(pts), "SetSampleTime");
            MFHelpers.Check(sample.SetSampleDuration(_frameDurationTicks), "SetSampleDuration");
            _frameIndex++;
        }

        private bool DrainOutput(out byte[] buffer, out int length, out bool isKeyframe)
        {
            buffer = null;
            length = 0;
            isKeyframe = false;

            var outputs = new MFT_OUTPUT_DATA_BUFFER[1];
            outputs[0].dwStreamID = 0;
            outputs[0].pSample = _encoderProvidesOutputSamples ? null : _outputSample;
            outputs[0].dwStatus = 0;
            outputs[0].pEvents = null;

            uint status;
            int hr = _encoder.ProcessOutput(0, 1, outputs, out status);

            if (hr == MFConstants.MF_E_TRANSFORM_NEED_MORE_INPUT)
            {
                return false;
            }

            if (hr == MFConstants.MF_E_TRANSFORM_STREAM_CHANGE)
            {
                IMFMediaType newType;
                MFHelpers.Check(_encoder.GetOutputAvailableType(0, 0, out newType), "GetOutputAvailableType");
                try
                {
                    MFHelpers.Check(_encoder.SetOutputType(0, newType, 0), "SetOutputType(updated)");
                }
                finally
                {
                    Marshal.ReleaseComObject(newType);
                }
                return false;
            }

            MFHelpers.Check(hr, "ProcessOutput");

            IMFSample sample = outputs[0].pSample;
            if (sample == null)
            {
                return false;
            }

            try
            {
                IMFMediaBuffer mediaBuffer;
                MFHelpers.Check(sample.ConvertToContiguousBuffer(out mediaBuffer), "ConvertToContiguousBuffer");
                try
                {
                    IntPtr p;
                    int maxLen, curLen;
                    MFHelpers.Check(mediaBuffer.Lock(out p, out maxLen, out curLen), "OutputBuffer.Lock");
                    try
                    {
                        if (curLen > 0)
                        {
                            buffer = new byte[curLen];
                            Marshal.Copy(p, buffer, 0, curLen);
                            length = curLen;
                        }
                    }
                    finally
                    {
                        MFHelpers.Check(mediaBuffer.Unlock(), "OutputBuffer.Unlock");
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(mediaBuffer);
                }

                int sampleFlags;
                if (sample.GetSampleFlags(out sampleFlags) == 0)
                {
                    isKeyframe = (sampleFlags & 0x1) != 0;
                }

                uint cleanPointSet;
                var cleanPointKey = new Guid("9cdf01d8-a0f0-43ba-b077-eaa06cbd728a");
                if (sample.GetUINT32(ref cleanPointKey, out cleanPointSet) == 0 && cleanPointSet != 0)
                {
                    isKeyframe = true;
                }
            }
            finally
            {
                if (_encoderProvidesOutputSamples && sample != null)
                {
                    Marshal.ReleaseComObject(sample);
                }
                else
                {
                    if (_outputBuffer != null)
                    {
                        _outputBuffer.SetCurrentLength(0);
                    }
                }
            }

            return length > 0;
        }
    }
}
