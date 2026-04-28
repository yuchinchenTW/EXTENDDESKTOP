using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ExtentDesktop.Shared;

namespace ExtentDesktop.Receiver
{
    internal sealed class H264Decoder : IDisposable
    {
        private readonly int _expectedWidth;
        private readonly int _expectedHeight;
        private readonly FrameBitmapPool _bitmapPool;
        private IMFTransform _decoder;
        private IMFMediaBuffer _outputBuffer;
        private IMFSample _outputSample;
        private bool _encoderProvidesOutputSamples;
        private bool _outputTypeSet;
        private int _frameWidth;
        private int _frameHeight;
        private int _frameStride;
        private bool _streaming;

        public H264Decoder(int expectedWidth, int expectedHeight, FrameBitmapPool bitmapPool)
        {
            _expectedWidth = expectedWidth;
            _expectedHeight = expectedHeight;
            _bitmapPool = bitmapPool;

            MFHelpers.Check(MFNative.MFStartup(MFConstants.MF_VERSION, MFConstants.MFSTARTUP_LITE), "MFStartup");

            try
            {
                CreateDecoder();
                StartStreaming();
            }
            catch
            {
                Cleanup();
                MFNative.MFShutdown();
                throw;
            }
        }

        public void Submit(byte[] data, int length)
        {
            IMFSample input;
            IMFMediaBuffer inBuffer;
            MFHelpers.Check(MFNative.MFCreateMemoryBuffer(length, out inBuffer), "MFCreateMemoryBuffer(input)");
            try
            {
                IntPtr p;
                int maxLen, curLen;
                MFHelpers.Check(inBuffer.Lock(out p, out maxLen, out curLen), "InputBuffer.Lock");
                try
                {
                    Marshal.Copy(data, 0, p, length);
                }
                finally
                {
                    MFHelpers.Check(inBuffer.Unlock(), "InputBuffer.Unlock");
                }
                MFHelpers.Check(inBuffer.SetCurrentLength(length), "InputBuffer.SetCurrentLength");

                MFHelpers.Check(MFNative.MFCreateSample(out input), "MFCreateSample(input)");
                try
                {
                    MFHelpers.Check(input.AddBuffer(inBuffer), "Input AddBuffer");

                    int hr = _decoder.ProcessInput(0, input, 0);
                    if (hr == MFConstants.MF_E_TRANSFORM_STREAM_CHANGE || hr == MFConstants.MF_E_INVALIDMEDIATYPE)
                    {
                        return;
                    }
                    MFHelpers.Check(hr, "ProcessInput");
                }
                finally
                {
                    Marshal.ReleaseComObject(input);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(inBuffer);
            }
        }

        public bool TryDrainBitmap(out Bitmap bitmap)
        {
            return DrainOutput(out bitmap);
        }

        public void Dispose()
        {
            try
            {
                if (_streaming && _decoder != null)
                {
                    _decoder.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_END_OF_STREAM, IntPtr.Zero);
                    _decoder.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_END_STREAMING, IntPtr.Zero);
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
            if (_decoder != null)
            {
                Marshal.ReleaseComObject(_decoder);
                _decoder = null;
            }
            _streaming = false;
        }

        private void CreateDecoder()
        {
            var clsid = MFGuids.CLSID_CMSH264DecoderMFT;
            var iid = new Guid("bf94c121-5b05-4e6f-8000-ba598961414d");
            object decoderObj;
            int hr = MFNative.CoCreateInstance(ref clsid, IntPtr.Zero, MFConstants.CLSCTX_INPROC_SERVER, ref iid, out decoderObj);
            MFHelpers.LogHr("CoCreateInstance(H264 Decoder)", hr);
            MFHelpers.Check(hr, "CoCreateInstance(H264 Decoder)");
            _decoder = (IMFTransform)decoderObj;

            SetDecoderLowLatency();
            SetInputTypeFromAvailable();
            TryNegotiateOutputType();
        }

        private void SetDecoderLowLatency()
        {
            try
            {
                IMFAttributes attrs;
                int hr = _decoder.GetAttributes(out attrs);
                MFHelpers.LogHr("decoder GetAttributes", hr);
                if (hr < 0 || attrs == null) return;

                try
                {
                    var lowLatencyKey = MFGuids.MF_LOW_LATENCY;
                    int setHr = attrs.SetUINT32(ref lowLatencyKey, 1);
                    MFHelpers.LogHr("decoder SetUINT32(MF_LOW_LATENCY)", setHr);
                }
                finally
                {
                    Marshal.ReleaseComObject(attrs);
                }
            }
            catch (Exception ex)
            {
                MFHelpers.Log("SetDecoderLowLatency threw: " + ex.Message);
            }
        }

        private void SetInputTypeFromAvailable()
        {
            for (int i = 0; i < 16; i++)
            {
                IMFMediaType template;
                int getHr = _decoder.GetInputAvailableType(0, i, out template);
                if (getHr < 0)
                {
                    MFHelpers.LogHr("decoder GetInputAvailableType[" + i + "]", getHr);
                    MFHelpers.Check(getHr, "GetInputAvailableType (no H264 found)");
                    return;
                }

                try
                {
                    var subKey = MFGuids.MF_MT_SUBTYPE;
                    Guid sub;
                    if (template.GetGUID(ref subKey, out sub) != 0) continue;
                    MFHelpers.Log("decoder input available[" + i + "] subtype=" + sub);
                    if (sub != MFGuids.MFVideoFormat_H264) continue;

                    var sizeKey = MFGuids.MF_MT_FRAME_SIZE;
                    template.SetUINT64(ref sizeKey, MFHelpers.PackUInt64((uint)_expectedWidth, (uint)_expectedHeight));

                    var rateKey = MFGuids.MF_MT_FRAME_RATE;
                    template.SetUINT64(ref rateKey, MFHelpers.PackUInt64(60, 1));

                    int setHr = _decoder.SetInputType(0, template, 0);
                    MFHelpers.LogHr("decoder SetInputType(template[" + i + "] H264)", setHr);
                    if (setHr >= 0) return;
                }
                finally
                {
                    Marshal.ReleaseComObject(template);
                }
            }

            MFHelpers.Check(unchecked((int)0x80004005), "decoder SetInputType(H264) [no template accepted]");
        }

        private void TryNegotiateOutputType()
        {
            for (int i = 0; ; i++)
            {
                IMFMediaType candidate;
                int hr = _decoder.GetOutputAvailableType(0, i, out candidate);
                if (hr < 0)
                {
                    break;
                }

                try
                {
                    var subtypeKey = MFGuids.MF_MT_SUBTYPE;
                    Guid subtype;
                    if (candidate.GetGUID(ref subtypeKey, out subtype) == 0 && subtype == MFGuids.MFVideoFormat_NV12)
                    {
                        if (_decoder.SetOutputType(0, candidate, 0) == 0)
                        {
                            _outputTypeSet = true;
                            CacheOutputDimensions(candidate);
                            return;
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(candidate);
                }
            }
        }

        private void CacheOutputDimensions(IMFMediaType type)
        {
            ulong sizePacked;
            var sizeKey = MFGuids.MF_MT_FRAME_SIZE;
            if (type.GetUINT64(ref sizeKey, out sizePacked) == 0)
            {
                _frameWidth = (int)(sizePacked >> 32);
                _frameHeight = (int)(sizePacked & 0xFFFFFFFF);
                _frameStride = _frameWidth;
            }

            uint stride;
            var strideKey = MFGuids.MF_MT_DEFAULT_STRIDE;
            if (type.GetUINT32(ref strideKey, out stride) == 0)
            {
                int s = unchecked((int)stride);
                if (s < 0) s = -s;
                if (s > 0) _frameStride = s;
            }
        }

        private void StartStreaming()
        {
            MFHelpers.Check(_decoder.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, IntPtr.Zero), "BEGIN_STREAMING");
            MFHelpers.Check(_decoder.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_START_OF_STREAM, IntPtr.Zero), "START_OF_STREAM");

            MFT_OUTPUT_STREAM_INFO info;
            MFHelpers.Check(_decoder.GetOutputStreamInfo(0, out info), "GetOutputStreamInfo");
            _encoderProvidesOutputSamples = (info.dwFlags & (MFConstants.MFT_OUTPUT_STREAM_PROVIDES_SAMPLES | MFConstants.MFT_OUTPUT_STREAM_CAN_PROVIDE_SAMPLES)) != 0;

            if (!_encoderProvidesOutputSamples)
            {
                int outBufferSize = info.cbSize > 0 ? info.cbSize : (_expectedWidth * _expectedHeight * 3 / 2);
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

            _streaming = true;
        }

        private bool DrainOutput(out Bitmap bitmap)
        {
            bitmap = null;

            while (true)
            {
                if (!_outputTypeSet)
                {
                    TryNegotiateOutputType();
                    if (!_outputTypeSet)
                    {
                        return false;
                    }
                }

                var outputs = new MFT_OUTPUT_DATA_BUFFER[1];
                outputs[0].dwStreamID = 0;
                outputs[0].pSample = _encoderProvidesOutputSamples ? null : _outputSample;
                outputs[0].dwStatus = 0;
                outputs[0].pEvents = null;

                uint status;
                int hr = _decoder.ProcessOutput(0, 1, outputs, out status);

                if (hr == MFConstants.MF_E_TRANSFORM_NEED_MORE_INPUT)
                {
                    return false;
                }

                if (hr == MFConstants.MF_E_TRANSFORM_STREAM_CHANGE)
                {
                    _outputTypeSet = false;
                    TryNegotiateOutputType();
                    continue;
                }

                MFHelpers.Check(hr, "ProcessOutput");

                IMFSample sample = outputs[0].pSample;
                if (sample == null)
                {
                    return false;
                }

                try
                {
                    bitmap = ConvertSampleToBitmap(sample);
                }
                finally
                {
                    if (_encoderProvidesOutputSamples && sample != null)
                    {
                        Marshal.ReleaseComObject(sample);
                    }
                    else if (_outputBuffer != null)
                    {
                        _outputBuffer.SetCurrentLength(0);
                    }
                }

                return bitmap != null;
            }
        }

        private Bitmap ConvertSampleToBitmap(IMFSample sample)
        {
            if (_frameWidth <= 0 || _frameHeight <= 0)
            {
                IMFMediaType currentType;
                if (_decoder.GetOutputCurrentType(0, out currentType) == 0)
                {
                    try
                    {
                        CacheOutputDimensions(currentType);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(currentType);
                    }
                }
            }

            int width = _frameWidth > 0 ? _frameWidth : _expectedWidth;
            int height = _frameHeight > 0 ? _frameHeight : _expectedHeight;
            int stride = _frameStride > 0 ? _frameStride : width;

            IMFMediaBuffer mediaBuffer;
            MFHelpers.Check(sample.ConvertToContiguousBuffer(out mediaBuffer), "ConvertToContiguousBuffer");
            try
            {
                IntPtr p;
                int maxLen, curLen;
                MFHelpers.Check(mediaBuffer.Lock(out p, out maxLen, out curLen), "OutputBuffer.Lock");
                try
                {
                    var bitmap = _bitmapPool != null ? _bitmapPool.Take(width, height) : new Bitmap(width, height, PixelFormat.Format32bppRgb);
                    if (_bitmapPool != null) bitmap.Tag = _bitmapPool;
                    var rect = new Rectangle(0, 0, width, height);
                    var bits = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                    try
                    {
                        PixelConvert.Nv12ToBgra32(p, stride, bits.Scan0, bits.Stride, width, height);
                    }
                    finally
                    {
                        bitmap.UnlockBits(bits);
                    }
                    return bitmap;
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
        }
    }
}
