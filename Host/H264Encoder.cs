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
        private IMFTransform _colorConverter;
        private IMFSample _outputSample;
        private IMFMediaBuffer _outputBuffer;
        private IMFMediaBuffer _inputBuffer;
        private int _inputBufferCapacity;
        private IMFMediaBuffer _bgraBuffer;
        private int _bgraBufferCapacity;
        private IMFSample _bgraSample;
        private IMFSample _nv12Sample;
        private bool _encoderProvidesOutputSamples;
        private long _frameIndex;
        private bool _streaming;
        private bool _converterStreaming;
        private byte[] _outputScratch = new byte[256 * 1024];

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
                CreateColorConverter();
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
            CopyBgraIntoBuffer(bgraData, bgraStride);

            long pts = _frameIndex * _frameDurationTicks;
            _frameIndex++;

            MFHelpers.Check(_bgraSample.SetSampleTime(pts), "BgraSample.SetSampleTime");
            MFHelpers.Check(_bgraSample.SetSampleDuration(_frameDurationTicks), "BgraSample.SetSampleDuration");

            int hr = _colorConverter.ProcessInput(0, _bgraSample, 0);
            MFHelpers.Check(hr, "ColorConvert ProcessInput");

            DrainColorConverterToEncoder(pts);
        }

        private void DrainColorConverterToEncoder(long pts)
        {
            while (true)
            {
                MFHelpers.Check(_inputBuffer.SetCurrentLength(0), "NV12 SetCurrentLength(0)");

                var outputs = new MFT_OUTPUT_DATA_BUFFER[1];
                outputs[0].dwStreamID = 0;
                outputs[0].pSample = _nv12Sample;
                outputs[0].dwStatus = 0;
                outputs[0].pEvents = null;

                uint status;
                int hr = _colorConverter.ProcessOutput(0, 1, outputs, out status);

                if (hr == MFConstants.MF_E_TRANSFORM_NEED_MORE_INPUT)
                {
                    return;
                }

                if (hr == MFConstants.MF_E_TRANSFORM_STREAM_CHANGE)
                {
                    IMFMediaType newType;
                    if (_colorConverter.GetOutputAvailableType(0, 0, out newType) == 0)
                    {
                        try { _colorConverter.SetOutputType(0, newType, 0); }
                        finally { Marshal.ReleaseComObject(newType); }
                    }
                    continue;
                }

                MFHelpers.Check(hr, "ColorConvert ProcessOutput");

                MFHelpers.Check(_nv12Sample.SetSampleTime(pts), "Nv12Sample.SetSampleTime");
                MFHelpers.Check(_nv12Sample.SetSampleDuration(_frameDurationTicks), "Nv12Sample.SetSampleDuration");

                int encHr = _encoder.ProcessInput(0, _nv12Sample, 0);
                MFHelpers.Check(encHr, "Encoder ProcessInput");
                return;
            }
        }

        private void CopyBgraIntoBuffer(IntPtr bgraData, int bgraStride)
        {
            IntPtr p;
            int maxLen, curLen;
            MFHelpers.Check(_bgraBuffer.Lock(out p, out maxLen, out curLen), "BgraBuffer.Lock");
            try
            {
                int dstStride = _width * 4;
                if (bgraStride == dstStride)
                {
                    MFCopyBytes(bgraData, p, _bgraBufferCapacity);
                }
                else
                {
                    for (int y = 0; y < _height; y++)
                    {
                        MFCopyBytes(IntPtr.Add(bgraData, y * bgraStride), IntPtr.Add(p, y * dstStride), dstStride);
                    }
                }
            }
            finally
            {
                MFHelpers.Check(_bgraBuffer.Unlock(), "BgraBuffer.Unlock");
            }
            MFHelpers.Check(_bgraBuffer.SetCurrentLength(_bgraBufferCapacity), "BgraBuffer.SetCurrentLength");
        }

        private static void MFCopyBytes(IntPtr src, IntPtr dst, int count)
        {
            RtlMoveMemory(dst, src, (UIntPtr)count);
        }

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory", SetLastError = false)]
        private static extern void RtlMoveMemory(IntPtr dest, IntPtr src, UIntPtr count);

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
                if (_converterStreaming && _colorConverter != null)
                {
                    _colorConverter.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_END_OF_STREAM, IntPtr.Zero);
                    _colorConverter.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_END_STREAMING, IntPtr.Zero);
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
            if (_nv12Sample != null)
            {
                Marshal.ReleaseComObject(_nv12Sample);
                _nv12Sample = null;
            }
            if (_inputBuffer != null)
            {
                Marshal.ReleaseComObject(_inputBuffer);
                _inputBuffer = null;
            }
            if (_bgraSample != null)
            {
                Marshal.ReleaseComObject(_bgraSample);
                _bgraSample = null;
            }
            if (_bgraBuffer != null)
            {
                Marshal.ReleaseComObject(_bgraBuffer);
                _bgraBuffer = null;
            }
            if (_colorConverter != null)
            {
                Marshal.ReleaseComObject(_colorConverter);
                _colorConverter = null;
            }
            if (_encoder != null)
            {
                Marshal.ReleaseComObject(_encoder);
                _encoder = null;
            }
            _streaming = false;
            _converterStreaming = false;
        }

        private void CreateEncoder(int bitrate)
        {
            MFHelpers.Log("=== H264Encoder.CreateEncoder begin: " + _width + "x" + _height + "@" + _fps + " bitrate=" + bitrate + " ===");

            var clsid = MFGuids.CLSID_CMSH264EncoderMFT;
            var iid = new Guid("bf94c121-5b05-4e6f-8000-ba598961414d");
            object encoderObj;
            int hr = MFNative.CoCreateInstance(ref clsid, IntPtr.Zero, MFConstants.CLSCTX_INPROC_SERVER, ref iid, out encoderObj);
            MFHelpers.LogHr("CoCreateInstance(H264 Encoder)", hr);
            MFHelpers.Check(hr, "CoCreateInstance(H264 Encoder)");
            _encoder = (IMFTransform)encoderObj;

            UnlockAsyncIfNeeded();
            ConfigureLowLatencyAttribute();
            SetOutputTypeWithFallbacks(bitrate);
            SetInputTypeFromAvailable();
        }

        private void CreateColorConverter()
        {
            MFHelpers.Log("=== H264Encoder.CreateColorConverter begin ===");

            var clsid = MFGuids.CLSID_CColorConvertDMO;
            var iid = new Guid("bf94c121-5b05-4e6f-8000-ba598961414d");
            object converterObj;
            int hr = MFNative.CoCreateInstance(ref clsid, IntPtr.Zero, MFConstants.CLSCTX_INPROC_SERVER, ref iid, out converterObj);
            MFHelpers.LogHr("CoCreateInstance(ColorConvert)", hr);
            MFHelpers.Check(hr, "CoCreateInstance(ColorConvert)");
            _colorConverter = (IMFTransform)converterObj;

            // Set input type RGB32 first (some converters require input first)
            var inputType = MFHelpers.CreateVideoType(MFGuids.MFVideoFormat_RGB32, _width, _height, _fps, 1);
            try
            {
                var strideKey = MFGuids.MF_MT_DEFAULT_STRIDE;
                inputType.SetUINT32(ref strideKey, (uint)(_width * 4));

                int inputHr = _colorConverter.SetInputType(0, inputType, 0);
                MFHelpers.LogHr("ColorConvert SetInputType(RGB32)", inputHr);
                MFHelpers.Check(inputHr, "ColorConvert SetInputType(RGB32)");
            }
            finally
            {
                Marshal.ReleaseComObject(inputType);
            }

            var outputType = MFHelpers.CreateVideoType(MFGuids.MFVideoFormat_NV12, _width, _height, _fps, 1);
            try
            {
                int outputHr = _colorConverter.SetOutputType(0, outputType, 0);
                MFHelpers.LogHr("ColorConvert SetOutputType(NV12)", outputHr);
                MFHelpers.Check(outputHr, "ColorConvert SetOutputType(NV12)");
            }
            finally
            {
                Marshal.ReleaseComObject(outputType);
            }
        }

        private void SetInputTypeFromAvailable()
        {
            for (int i = 0; i < 16; i++)
            {
                IMFMediaType template;
                int getHr = _encoder.GetInputAvailableType(0, i, out template);
                if (getHr < 0)
                {
                    MFHelpers.LogHr("GetInputAvailableType[" + i + "]", getHr);
                    MFHelpers.Check(getHr, "GetInputAvailableType (no NV12 found)");
                    return;
                }

                try
                {
                    var subKey = MFGuids.MF_MT_SUBTYPE;
                    Guid sub;
                    if (template.GetGUID(ref subKey, out sub) != 0) continue;
                    MFHelpers.Log("input available[" + i + "] subtype=" + sub);
                    if (sub != MFGuids.MFVideoFormat_NV12) continue;

                    var sizeKey = MFGuids.MF_MT_FRAME_SIZE;
                    template.SetUINT64(ref sizeKey, MFHelpers.PackUInt64((uint)_width, (uint)_height));

                    var rateKey = MFGuids.MF_MT_FRAME_RATE;
                    template.SetUINT64(ref rateKey, MFHelpers.PackUInt64((uint)_fps, 1));

                    var aspectKey = MFGuids.MF_MT_PIXEL_ASPECT_RATIO;
                    template.SetUINT64(ref aspectKey, MFHelpers.PackUInt64(1, 1));

                    var interlaceKey = MFGuids.MF_MT_INTERLACE_MODE;
                    template.SetUINT32(ref interlaceKey, (uint)MFConstants.MFVideoInterlace_Progressive);

                    int setHr = _encoder.SetInputType(0, template, 0);
                    MFHelpers.LogHr("SetInputType(template[" + i + "] NV12)", setHr);
                    if (setHr >= 0) return;
                }
                finally
                {
                    Marshal.ReleaseComObject(template);
                }
            }

            MFHelpers.Check(unchecked((int)0x80004005), "SetInputType(NV12) [no template accepted]");
        }

        private void UnlockAsyncIfNeeded()
        {
            try
            {
                IMFAttributes attrs;
                int hr = _encoder.GetAttributes(out attrs);
                MFHelpers.LogHr("GetAttributes (for ASYNC check)", hr);
                if (hr < 0 || attrs == null) return;

                try
                {
                    uint asyncFlag = 0;
                    var asyncKey = MFGuids.MFT_TRANSFORM_ASYNC;
                    int getHr = attrs.GetUINT32(ref asyncKey, out asyncFlag);
                    MFHelpers.Log("MFT_TRANSFORM_ASYNC getHr=0x" + getHr.ToString("X8") + " value=" + asyncFlag);

                    if (getHr == 0 && asyncFlag != 0)
                    {
                        var unlockKey = MFGuids.MFT_TRANSFORM_ASYNC_UNLOCK;
                        int setHr = attrs.SetUINT32(ref unlockKey, 1);
                        MFHelpers.LogHr("SetUINT32(ASYNC_UNLOCK)", setHr);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(attrs);
                }
            }
            catch (Exception ex)
            {
                MFHelpers.Log("UnlockAsyncIfNeeded threw: " + ex.Message);
            }
        }

        private void ConfigureLowLatencyAttribute()
        {
            try
            {
                IMFAttributes attrs;
                if (_encoder.GetAttributes(out attrs) != 0 || attrs == null) return;

                try
                {
                    var lowLatencyAttr = MFGuids.MF_LOW_LATENCY;
                    int hr = attrs.SetUINT32(ref lowLatencyAttr, 1);
                    MFHelpers.LogHr("SetUINT32(MF_LOW_LATENCY)", hr);
                }
                finally
                {
                    Marshal.ReleaseComObject(attrs);
                }
            }
            catch (Exception ex)
            {
                MFHelpers.Log("ConfigureLowLatencyAttribute threw: " + ex.Message);
            }
        }

        private void SetOutputTypeWithFallbacks(int bitrate)
        {
            int hr;

            MFHelpers.Log("Trying SetOutputType: no profile, no level, bitrate=" + bitrate);
            hr = TrySetOutputType(bitrate, profile: 0, level: 0);
            MFHelpers.LogHr("  result", hr);
            if (hr >= 0) return;

            MFHelpers.Log("Trying SetOutputType: Base profile + Level 4.2, bitrate=" + bitrate);
            hr = TrySetOutputType(bitrate, profile: MFConstants.eAVEncH264VProfile_Base, level: 42);
            MFHelpers.LogHr("  result", hr);
            if (hr >= 0) return;

            MFHelpers.Log("Trying SetOutputType: Main profile, bitrate=" + bitrate);
            hr = TrySetOutputType(bitrate, profile: MFConstants.eAVEncH264VProfile_Main, level: 0);
            MFHelpers.LogHr("  result", hr);
            if (hr >= 0) return;

            int halfBitrate = Math.Max(1500000, bitrate / 2);
            MFHelpers.Log("Trying SetOutputType: no profile, halved bitrate=" + halfBitrate);
            hr = TrySetOutputType(halfBitrate, profile: 0, level: 0);
            MFHelpers.LogHr("  result", hr);
            if (hr >= 0) return;

            MFHelpers.Log("Trying SetOutputType: from GetOutputAvailableType template");
            hr = TrySetOutputTypeFromAvailable(bitrate);
            MFHelpers.LogHr("  result", hr);
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
                if (hr < 0) { MFHelpers.LogHr("  SetUINT32(BITRATE)", hr); return hr; }

                if (profile != 0)
                {
                    var profileKey = MFGuids.MF_MT_MPEG2_PROFILE;
                    hr = outputType.SetUINT32(ref profileKey, (uint)profile);
                    if (hr < 0) { MFHelpers.LogHr("  SetUINT32(PROFILE)", hr); return hr; }
                }

                if (level != 0)
                {
                    var levelKey = MFGuids.MF_MT_MPEG2_LEVEL;
                    hr = outputType.SetUINT32(ref levelKey, (uint)level);
                    if (hr < 0) { MFHelpers.LogHr("  SetUINT32(LEVEL)", hr); return hr; }
                }

                return _encoder.SetOutputType(0, outputType, 0);
            }
            finally
            {
                Marshal.ReleaseComObject(outputType);
            }
        }

        private int TrySetOutputTypeFromAvailable(int bitrate)
        {
            for (int i = 0; i < 8; i++)
            {
                IMFMediaType template;
                int getHr = _encoder.GetOutputAvailableType(0, i, out template);
                if (getHr < 0)
                {
                    MFHelpers.LogHr("  GetOutputAvailableType[" + i + "]", getHr);
                    return getHr;
                }

                try
                {
                    var subKey = MFGuids.MF_MT_SUBTYPE;
                    Guid sub;
                    if (template.GetGUID(ref subKey, out sub) != 0) continue;
                    MFHelpers.Log("  available[" + i + "] subtype=" + sub);
                    if (sub != MFGuids.MFVideoFormat_H264) continue;

                    var majorKey = MFGuids.MF_MT_MAJOR_TYPE;
                    var majorVal = MFGuids.MFMediaType_Video;
                    template.SetGUID(ref majorKey, ref majorVal);

                    var sizeKey = MFGuids.MF_MT_FRAME_SIZE;
                    template.SetUINT64(ref sizeKey, MFHelpers.PackUInt64((uint)_width, (uint)_height));

                    var rateKey = MFGuids.MF_MT_FRAME_RATE;
                    template.SetUINT64(ref rateKey, MFHelpers.PackUInt64((uint)_fps, 1));

                    var aspectKey = MFGuids.MF_MT_PIXEL_ASPECT_RATIO;
                    template.SetUINT64(ref aspectKey, MFHelpers.PackUInt64(1, 1));

                    var interlaceKey = MFGuids.MF_MT_INTERLACE_MODE;
                    template.SetUINT32(ref interlaceKey, (uint)MFConstants.MFVideoInterlace_Progressive);

                    var bitrateKey = MFGuids.MF_MT_AVG_BITRATE;
                    template.SetUINT32(ref bitrateKey, (uint)bitrate);

                    int setHr = _encoder.SetOutputType(0, template, 0);
                    MFHelpers.LogHr("  SetOutputType(template[" + i + "])", setHr);
                    if (setHr >= 0) return setHr;
                }
                finally
                {
                    Marshal.ReleaseComObject(template);
                }
            }

            return unchecked((int)0x80004005);
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
                var bFramesKey = MFGuids.CODECAPI_AVEncMPVDefaultBPictureCount;
                var qualityKey = MFGuids.CODECAPI_AVEncCommonQuality;

                SetVariantBool(codecApi, ref lowLatencyKey, true);
                SetVariantUInt32(codecApi, ref bFramesKey, 0);
                SetVariantUInt32(codecApi, ref rateModeKey, (uint)MFConstants.eAVEncCommonRateControlMode_Quality);
                SetVariantUInt32(codecApi, ref qualityKey, 95);
                SetVariantUInt32(codecApi, ref gopKey, (uint)(_fps * 5));
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
            MFHelpers.Check(MFNative.MFCreateMemoryBuffer(nv12Size, out _inputBuffer), "MFCreateMemoryBuffer(input/nv12)");

            _bgraBufferCapacity = _width * 4 * _height;
            MFHelpers.Check(MFNative.MFCreateMemoryBuffer(_bgraBufferCapacity, out _bgraBuffer), "MFCreateMemoryBuffer(bgra)");

            MFHelpers.Check(MFNative.MFCreateSample(out _bgraSample), "MFCreateSample(bgra)");
            MFHelpers.Check(_bgraSample.AddBuffer(_bgraBuffer), "BGRA AddBuffer");

            MFHelpers.Check(MFNative.MFCreateSample(out _nv12Sample), "MFCreateSample(nv12)");
            MFHelpers.Check(_nv12Sample.AddBuffer(_inputBuffer), "NV12 AddBuffer");

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
            int convHr = _colorConverter.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, IntPtr.Zero);
            MFHelpers.LogHr("ColorConvert BEGIN_STREAMING", convHr);
            MFHelpers.Check(convHr, "ColorConvert BEGIN_STREAMING");

            convHr = _colorConverter.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_START_OF_STREAM, IntPtr.Zero);
            MFHelpers.LogHr("ColorConvert START_OF_STREAM", convHr);
            MFHelpers.Check(convHr, "ColorConvert START_OF_STREAM");

            _converterStreaming = true;

            int hr = _encoder.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, IntPtr.Zero);
            MFHelpers.LogHr("BEGIN_STREAMING", hr);
            MFHelpers.Check(hr, "BEGIN_STREAMING");

            hr = _encoder.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_START_OF_STREAM, IntPtr.Zero);
            MFHelpers.LogHr("START_OF_STREAM", hr);
            MFHelpers.Check(hr, "START_OF_STREAM");

            _streaming = true;
            MFHelpers.Log("=== H264Encoder ready (with ColorConvert) ===");
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
                            if (curLen > _outputScratch.Length)
                            {
                                _outputScratch = new byte[Math.Max(curLen, _outputScratch.Length * 2)];
                            }
                            Marshal.Copy(p, _outputScratch, 0, curLen);
                            buffer = _outputScratch;
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
