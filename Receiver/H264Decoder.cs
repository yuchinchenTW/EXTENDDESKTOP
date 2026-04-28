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
        private IMFTransform _colorConverter;
        private IMFMediaBuffer _outputBuffer;
        private IMFSample _outputSample;
        private IMFMediaBuffer _bgraBuffer;
        private IMFSample _bgraSample;
        private bool _encoderProvidesOutputSamples;
        private bool _outputTypeSet;
        private bool _converterReady;
        private int _frameWidth;
        private int _frameHeight;
        private int _frameStride;
        private bool _streaming;
        private bool _converterStreaming;
        private IMFMediaBuffer _persistentInputBuffer;
        private IMFSample _persistentInputSample;
        private int _persistentInputCapacity;
        private IMFDXGIDeviceManager _dxgiManager;
        private object _d3dDevice;

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
            EnsureInputBuffer(length);

            IntPtr p;
            int maxLen, curLen;
            MFHelpers.Check(_persistentInputBuffer.Lock(out p, out maxLen, out curLen), "InputBuffer.Lock");
            try
            {
                Marshal.Copy(data, 0, p, length);
            }
            finally
            {
                MFHelpers.Check(_persistentInputBuffer.Unlock(), "InputBuffer.Unlock");
            }
            MFHelpers.Check(_persistentInputBuffer.SetCurrentLength(length), "InputBuffer.SetCurrentLength");

            int hr = _decoder.ProcessInput(0, _persistentInputSample, 0);
            if (hr == MFConstants.MF_E_TRANSFORM_STREAM_CHANGE || hr == MFConstants.MF_E_INVALIDMEDIATYPE)
            {
                return;
            }
            MFHelpers.Check(hr, "ProcessInput");
        }

        private void EnsureInputBuffer(int length)
        {
            if (_persistentInputBuffer != null && length <= _persistentInputCapacity)
            {
                return;
            }

            if (_persistentInputSample != null)
            {
                Marshal.ReleaseComObject(_persistentInputSample);
                _persistentInputSample = null;
            }
            if (_persistentInputBuffer != null)
            {
                Marshal.ReleaseComObject(_persistentInputBuffer);
                _persistentInputBuffer = null;
            }

            int capacity = Math.Max(length, 1024 * 1024);
            MFHelpers.Check(MFNative.MFCreateMemoryBuffer(capacity, out _persistentInputBuffer), "MFCreateMemoryBuffer(persistent input)");
            MFHelpers.Check(MFNative.MFCreateSample(out _persistentInputSample), "MFCreateSample(persistent input)");
            MFHelpers.Check(_persistentInputSample.AddBuffer(_persistentInputBuffer), "Persistent input AddBuffer");
            _persistentInputCapacity = capacity;
        }

        internal long LastProcessOutputTicks;
        internal long LastConvertTicks;
        internal long AccumulatedProcessOutputTicks;
        internal long AccumulatedConvertTicks;

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
            if (_persistentInputSample != null)
            {
                Marshal.ReleaseComObject(_persistentInputSample);
                _persistentInputSample = null;
            }
            if (_persistentInputBuffer != null)
            {
                Marshal.ReleaseComObject(_persistentInputBuffer);
                _persistentInputBuffer = null;
            }
            if (_decoder != null)
            {
                Marshal.ReleaseComObject(_decoder);
                _decoder = null;
            }
            if (_dxgiManager != null)
            {
                Marshal.ReleaseComObject(_dxgiManager);
                _dxgiManager = null;
            }
            if (_d3dDevice != null)
            {
                Marshal.ReleaseComObject(_d3dDevice);
                _d3dDevice = null;
            }
            _streaming = false;
            _converterStreaming = false;
            _converterReady = false;
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

            TryAttachD3DManager();
            SetDecoderLowLatency();
            SetInputTypeFromAvailable();
            TryNegotiateOutputType();
        }

        private void TryAttachD3DManager()
        {
            try
            {
                const int D3D_DRIVER_TYPE_HARDWARE = 1;
                const int D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
                const int D3D11_CREATE_DEVICE_VIDEO_SUPPORT = 0x800;
                const int D3D11_SDK_VERSION = 7;

                object device, ctx;
                int featureLevel;
                int hr = MFNative.D3D11CreateDevice(
                    IntPtr.Zero,
                    D3D_DRIVER_TYPE_HARDWARE,
                    IntPtr.Zero,
                    D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
                    IntPtr.Zero, 0, D3D11_SDK_VERSION,
                    out device, out featureLevel, out ctx);
                MFHelpers.LogHr("D3D11CreateDevice", hr);
                if (hr < 0 || device == null) return;

                if (ctx != null) { Marshal.ReleaseComObject(ctx); }

                try
                {
                    var mt = device as ID3D10Multithread;
                    if (mt != null)
                    {
                        mt.SetMultithreadProtected(true);
                        MFHelpers.Log("ID3D10Multithread.SetMultithreadProtected(true)");
                    }
                }
                catch (Exception ex)
                {
                    MFHelpers.Log("Multithread protect failed: " + ex.Message);
                }

                int resetToken;
                IMFDXGIDeviceManager mgr;
                hr = MFNative.MFCreateDXGIDeviceManager(out resetToken, out mgr);
                MFHelpers.LogHr("MFCreateDXGIDeviceManager", hr);
                if (hr < 0)
                {
                    Marshal.ReleaseComObject(device);
                    return;
                }

                hr = mgr.ResetDevice(device, resetToken);
                MFHelpers.LogHr("DXGIManager.ResetDevice", hr);
                if (hr < 0)
                {
                    Marshal.ReleaseComObject(mgr);
                    Marshal.ReleaseComObject(device);
                    return;
                }

                IntPtr mgrPtr = Marshal.GetIUnknownForObject(mgr);
                try
                {
                    hr = _decoder.ProcessMessage(MFConstants.MFT_MESSAGE_SET_D3D_MANAGER, mgrPtr);
                    MFHelpers.LogHr("SET_D3D_MANAGER on decoder", hr);
                }
                finally
                {
                    Marshal.Release(mgrPtr);
                }

                if (hr < 0)
                {
                    Marshal.ReleaseComObject(mgr);
                    Marshal.ReleaseComObject(device);
                    return;
                }

                _dxgiManager = mgr;
                _d3dDevice = device;
                MFHelpers.Log("=== D3D11 manager attached: decoder runs DXVA HW path ===");
            }
            catch (Exception ex)
            {
                MFHelpers.Log("TryAttachD3DManager threw: " + ex.Message);
            }
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
                long tBefore = System.Diagnostics.Stopwatch.GetTimestamp();
                int hr = _decoder.ProcessOutput(0, 1, outputs, out status);
                long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - tBefore;
                LastProcessOutputTicks = elapsed;
                AccumulatedProcessOutputTicks += elapsed;

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
                    long tConv = System.Diagnostics.Stopwatch.GetTimestamp();
                    bitmap = ConvertSampleToBitmap(sample);
                    long convElapsed = System.Diagnostics.Stopwatch.GetTimestamp() - tConv;
                    LastConvertTicks = convElapsed;
                    AccumulatedConvertTicks += convElapsed;
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

            EnsureColorConverter(width, height);

            int hr = _colorConverter.ProcessInput(0, sample, 0);
            MFHelpers.Check(hr, "ColorConvert ProcessInput");

            int bgraSize = width * 4 * height;
            MFHelpers.Check(_bgraBuffer.SetCurrentLength(0), "BgraBuffer.SetCurrentLength(0)");

            var outputs = new MFT_OUTPUT_DATA_BUFFER[1];
            outputs[0].dwStreamID = 0;
            outputs[0].pSample = _bgraSample;
            outputs[0].dwStatus = 0;
            outputs[0].pEvents = null;

            uint status;
            int outHr = _colorConverter.ProcessOutput(0, 1, outputs, out status);
            if (outHr == MFConstants.MF_E_TRANSFORM_STREAM_CHANGE)
            {
                IMFMediaType newType;
                if (_colorConverter.GetOutputAvailableType(0, 0, out newType) == 0)
                {
                    try { _colorConverter.SetOutputType(0, newType, 0); }
                    finally { Marshal.ReleaseComObject(newType); }
                }
                outHr = _colorConverter.ProcessOutput(0, 1, outputs, out status);
            }
            MFHelpers.Check(outHr, "ColorConvert ProcessOutput");

            IntPtr bgraPtr;
            int bgraMaxLen, bgraCurLen;
            MFHelpers.Check(_bgraBuffer.Lock(out bgraPtr, out bgraMaxLen, out bgraCurLen), "BgraBuffer.Lock");
            try
            {
                var bitmap = _bitmapPool != null ? _bitmapPool.Take(width, height) : new Bitmap(width, height, PixelFormat.Format32bppRgb);
                if (_bitmapPool != null) bitmap.Tag = _bitmapPool;
                var rect = new Rectangle(0, 0, width, height);
                var bits = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                try
                {
                    int srcStride = width * 4;
                    if (bits.Stride == srcStride)
                    {
                        RtlMoveMemory(bits.Scan0, bgraPtr, (UIntPtr)bgraSize);
                    }
                    else
                    {
                        for (int y = 0; y < height; y++)
                        {
                            RtlMoveMemory(IntPtr.Add(bits.Scan0, y * bits.Stride), IntPtr.Add(bgraPtr, y * srcStride), (UIntPtr)srcStride);
                        }
                    }
                }
                finally
                {
                    bitmap.UnlockBits(bits);
                }
                return bitmap;
            }
            finally
            {
                MFHelpers.Check(_bgraBuffer.Unlock(), "BgraBuffer.Unlock");
            }
        }

        private void EnsureColorConverter(int width, int height)
        {
            if (_converterReady) return;

            MFHelpers.Log("=== H264Decoder.CreateColorConverter " + width + "x" + height + " ===");

            var clsid = MFGuids.CLSID_CColorConvertDMO;
            var iid = new Guid("bf94c121-5b05-4e6f-8000-ba598961414d");
            object converterObj;
            int hr = MFNative.CoCreateInstance(ref clsid, IntPtr.Zero, MFConstants.CLSCTX_INPROC_SERVER, ref iid, out converterObj);
            MFHelpers.LogHr("CoCreateInstance(ColorConvert)", hr);
            MFHelpers.Check(hr, "CoCreateInstance(ColorConvert)");
            _colorConverter = (IMFTransform)converterObj;

            int fps = 60;
            MFHelpers.SetConverterTypeFromAvailable(_colorConverter, isInput: true, subtype: MFGuids.MFVideoFormat_NV12, width: width, height: height, fps: fps, includeStride: false, label: "decoder ColorConvert input");
            MFHelpers.SetConverterTypeFromAvailable(_colorConverter, isInput: false, subtype: MFGuids.MFVideoFormat_RGB32, width: width, height: height, fps: fps, includeStride: true, label: "decoder ColorConvert output");

            int bgraSize = width * 4 * height;
            MFHelpers.Check(MFNative.MFCreateMemoryBuffer(bgraSize, out _bgraBuffer), "MFCreateMemoryBuffer(bgra)");
            MFHelpers.Check(MFNative.MFCreateSample(out _bgraSample), "MFCreateSample(bgra)");
            MFHelpers.Check(_bgraSample.AddBuffer(_bgraBuffer), "BGRA AddBuffer");

            int beginHr = _colorConverter.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, IntPtr.Zero);
            MFHelpers.LogHr("ColorConvert BEGIN_STREAMING", beginHr);
            MFHelpers.Check(beginHr, "ColorConvert BEGIN_STREAMING");

            int startHr = _colorConverter.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_START_OF_STREAM, IntPtr.Zero);
            MFHelpers.LogHr("ColorConvert START_OF_STREAM", startHr);
            MFHelpers.Check(startHr, "ColorConvert START_OF_STREAM");

            _converterStreaming = true;
            _converterReady = true;
            MFHelpers.Log("=== H264Decoder.ColorConverter ready ===");
        }

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory", SetLastError = false)]
        private static extern void RtlMoveMemory(IntPtr dest, IntPtr src, UIntPtr count);
    }
}
