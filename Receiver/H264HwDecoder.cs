using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ExtentDesktop.Shared;

namespace ExtentDesktop.Receiver
{
    internal sealed class H264HwDecoder : IDisposable
    {
        private readonly int _expectedWidth;
        private readonly int _expectedHeight;
        private readonly FrameBitmapPool _bitmapPool;

        private IMFTransform _decoder;
        private IMFTransform _converter;
        private IMFMediaEventGenerator _decoderEvents;
        private IMFMediaEventGenerator _converterEvents;
        private IMFDXGIDeviceManager _dxgiManager;
        private object _d3dDevice;

        private IMFMediaBuffer _persistentInputBuffer;
        private IMFSample _persistentInputSample;
        private int _persistentInputCapacity;

        private int _frameWidth;
        private int _frameHeight;
        private bool _streaming;

        internal long LastProcessOutputTicks;
        internal long LastConvertTicks;
        internal long AccumulatedProcessOutputTicks;
        internal long AccumulatedConvertTicks;

        public H264HwDecoder(int expectedWidth, int expectedHeight, FrameBitmapPool pool)
        {
            _expectedWidth = expectedWidth;
            _expectedHeight = expectedHeight;
            _bitmapPool = pool;
            _frameWidth = expectedWidth;
            _frameHeight = expectedHeight;

            MFHelpers.Check(MFNative.MFStartup(MFConstants.MF_VERSION, MFConstants.MFSTARTUP_LITE), "MFStartup");

            try
            {
                CreateD3DDevice();
                ActivateHwDecoder();
                ConfigureDecoderTypes();
                ActivateConverter();
                ConfigureConverterTypes();
                StartStreaming();
            }
            catch
            {
                Cleanup();
                try { MFNative.MFShutdown(); } catch { }
                throw;
            }
        }

        public void Submit(byte[] data, int length)
        {
            EnsureInputBuffer(length);

            IntPtr p;
            int maxLen, curLen;
            MFHelpers.Check(_persistentInputBuffer.Lock(out p, out maxLen, out curLen), "Hw InputBuffer.Lock");
            try
            {
                Marshal.Copy(data, 0, p, length);
            }
            finally
            {
                MFHelpers.Check(_persistentInputBuffer.Unlock(), "Hw InputBuffer.Unlock");
            }
            MFHelpers.Check(_persistentInputBuffer.SetCurrentLength(length), "Hw InputBuffer.SetCurrentLength");

            WaitForEvent(_decoderEvents, MediaEventTypes.METransformNeedInput, "decoder NeedInput");
            int hr = _decoder.ProcessInput(0, _persistentInputSample, 0);
            if (hr < 0)
            {
                MFHelpers.LogHr("Hw decoder ProcessInput", hr);
            }
        }

        public bool TryDrainBitmap(out Bitmap bitmap)
        {
            bitmap = null;

            int decType = TryGetEventNonBlocking(_decoderEvents);
            if (decType != MediaEventTypes.METransformHaveOutput) return false;

            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            IMFSample nv12Sample;
            if (!ProcessOutputDecoder(out nv12Sample)) return false;
            LastProcessOutputTicks = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            AccumulatedProcessOutputTicks += LastProcessOutputTicks;

            try
            {
                long tConv = System.Diagnostics.Stopwatch.GetTimestamp();
                bitmap = ConvertToBitmap(nv12Sample);
                LastConvertTicks = System.Diagnostics.Stopwatch.GetTimestamp() - tConv;
                AccumulatedConvertTicks += LastConvertTicks;
            }
            finally
            {
                if (nv12Sample != null) Marshal.ReleaseComObject(nv12Sample);
            }

            return bitmap != null;
        }

        public void Dispose()
        {
            try
            {
                if (_streaming)
                {
                    if (_decoder != null)
                    {
                        _decoder.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_END_OF_STREAM, IntPtr.Zero);
                        _decoder.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_END_STREAMING, IntPtr.Zero);
                    }
                    if (_converter != null)
                    {
                        _converter.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_END_OF_STREAM, IntPtr.Zero);
                        _converter.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_END_STREAMING, IntPtr.Zero);
                    }
                }
            }
            catch { }

            Cleanup();
            try { MFNative.MFShutdown(); } catch { }
        }

        private void Cleanup()
        {
            if (_persistentInputSample != null) { try { Marshal.ReleaseComObject(_persistentInputSample); } catch { } _persistentInputSample = null; }
            if (_persistentInputBuffer != null) { try { Marshal.ReleaseComObject(_persistentInputBuffer); } catch { } _persistentInputBuffer = null; }
            if (_decoderEvents != null) { try { Marshal.ReleaseComObject(_decoderEvents); } catch { } _decoderEvents = null; }
            if (_converterEvents != null) { try { Marshal.ReleaseComObject(_converterEvents); } catch { } _converterEvents = null; }
            if (_decoder != null) { try { Marshal.ReleaseComObject(_decoder); } catch { } _decoder = null; }
            if (_converter != null) { try { Marshal.ReleaseComObject(_converter); } catch { } _converter = null; }
            if (_dxgiManager != null) { try { Marshal.ReleaseComObject(_dxgiManager); } catch { } _dxgiManager = null; }
            if (_d3dDevice != null) { try { Marshal.ReleaseComObject(_d3dDevice); } catch { } _d3dDevice = null; }
            _streaming = false;
        }

        private void CreateD3DDevice()
        {
            const int D3D_DRIVER_TYPE_HARDWARE = 1;
            const int D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
            const int D3D11_CREATE_DEVICE_VIDEO_SUPPORT = 0x800;
            const int D3D11_SDK_VERSION = 7;

            object device, ctx;
            int featureLevel;
            int hr = MFNative.D3D11CreateDevice(
                IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero,
                D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
                IntPtr.Zero, 0, D3D11_SDK_VERSION,
                out device, out featureLevel, out ctx);
            MFHelpers.LogHr("Hw D3D11CreateDevice", hr);
            MFHelpers.Check(hr, "D3D11CreateDevice");

            if (ctx != null) Marshal.ReleaseComObject(ctx);

            try
            {
                var mt = device as ID3D10Multithread;
                if (mt != null) mt.SetMultithreadProtected(true);
            }
            catch { }

            int resetToken;
            IMFDXGIDeviceManager mgr;
            MFHelpers.Check(MFNative.MFCreateDXGIDeviceManager(out resetToken, out mgr), "MFCreateDXGIDeviceManager");
            MFHelpers.Check(mgr.ResetDevice(device, resetToken), "DXGIManager.ResetDevice");

            _d3dDevice = device;
            _dxgiManager = mgr;
        }

        private void ActivateHwDecoder()
        {
            var inputType = new MFT_REGISTER_TYPE_INFO
            {
                guidMajorType = MFGuids.MFMediaType_Video,
                guidSubtype = MFGuids.MFVideoFormat_H264
            };

            IntPtr inputTypePtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(MFT_REGISTER_TYPE_INFO)));
            IntPtr arrayPtr;
            int count;
            try
            {
                Marshal.StructureToPtr(inputType, inputTypePtr, false);
                int hr = MFNative.MFTEnumEx(
                    MFGuids.MFT_CATEGORY_VIDEO_DECODER,
                    (int)(MFT_ENUM_FLAG.Hardware | MFT_ENUM_FLAG.SortAndFilter),
                    inputTypePtr,
                    IntPtr.Zero,
                    out arrayPtr, out count);
                MFHelpers.Log("MFTEnumEx HW H264 decoder hr=0x" + hr.ToString("X8") + " count=" + count);
                MFHelpers.Check(hr, "MFTEnumEx HW decoder");
                if (count == 0)
                {
                    if (arrayPtr != IntPtr.Zero) MFNative.CoTaskMemFree(arrayPtr);
                    throw new InvalidOperationException("No HW H.264 decoder found");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(inputTypePtr);
            }

            try
            {
                IntPtr activatePtr = Marshal.ReadIntPtr(arrayPtr, 0);
                for (int i = 1; i < count; i++)
                {
                    IntPtr extra = Marshal.ReadIntPtr(arrayPtr, i * IntPtr.Size);
                    if (extra != IntPtr.Zero) Marshal.Release(extra);
                }

                var activate = (IMFActivate)Marshal.GetObjectForIUnknown(activatePtr);
                try
                {
                    var nameKey = MFGuids.MFT_FRIENDLY_NAME_Attribute;
                    int len;
                    if (activate.GetStringLength(ref nameKey, out len) == 0 && len > 0)
                    {
                        var sb = new System.Text.StringBuilder(len + 1);
                        int actLen;
                        if (activate.GetString(ref nameKey, sb, sb.Capacity, out actLen) == 0)
                        {
                            MFHelpers.Log("Activating HW decoder: " + sb.ToString());
                        }
                    }

                    var iid = new Guid("bf94c121-5b05-4e6f-8000-ba598961414d");
                    object decoderObj;
                    int hr = activate.ActivateObject(ref iid, out decoderObj);
                    MFHelpers.LogHr("HW decoder ActivateObject", hr);
                    MFHelpers.Check(hr, "ActivateObject(HW decoder)");
                    _decoder = (IMFTransform)decoderObj;
                }
                finally
                {
                    Marshal.ReleaseComObject(activate);
                    Marshal.Release(activatePtr);
                }
            }
            finally
            {
                MFNative.CoTaskMemFree(arrayPtr);
            }

            IMFAttributes attrs;
            MFHelpers.Check(_decoder.GetAttributes(out attrs), "HW decoder GetAttributes");
            try
            {
                var unlockKey = MFGuids.MFT_TRANSFORM_ASYNC_UNLOCK;
                MFHelpers.Check(attrs.SetUINT32(ref unlockKey, 1), "HW decoder ASYNC_UNLOCK");
                MFHelpers.Log("HW decoder ASYNC_UNLOCK set");

                var lowLatencyKey = MFGuids.MF_LOW_LATENCY;
                attrs.SetUINT32(ref lowLatencyKey, 1);
            }
            finally
            {
                Marshal.ReleaseComObject(attrs);
            }

            IntPtr mgrPtr = Marshal.GetIUnknownForObject(_dxgiManager);
            try
            {
                MFHelpers.Check(_decoder.ProcessMessage(MFConstants.MFT_MESSAGE_SET_D3D_MANAGER, mgrPtr), "HW decoder SET_D3D_MANAGER");
            }
            finally
            {
                Marshal.Release(mgrPtr);
            }

            _decoderEvents = (IMFMediaEventGenerator)_decoder;
            MFHelpers.Log("HW decoder events obtained");
        }

        private void ConfigureDecoderTypes()
        {
            for (int i = 0; i < 16; i++)
            {
                IMFMediaType template;
                int getHr = _decoder.GetInputAvailableType(0, i, out template);
                if (getHr < 0)
                {
                    MFHelpers.Check(getHr, "HW decoder no H264 input type");
                    return;
                }

                try
                {
                    var subKey = MFGuids.MF_MT_SUBTYPE;
                    Guid sub;
                    if (template.GetGUID(ref subKey, out sub) != 0) continue;
                    if (sub != MFGuids.MFVideoFormat_H264) continue;

                    var sizeKey = MFGuids.MF_MT_FRAME_SIZE;
                    template.SetUINT64(ref sizeKey, MFHelpers.PackUInt64((uint)_expectedWidth, (uint)_expectedHeight));
                    var rateKey = MFGuids.MF_MT_FRAME_RATE;
                    template.SetUINT64(ref rateKey, MFHelpers.PackUInt64(60, 1));

                    int sHr = _decoder.SetInputType(0, template, 0);
                    MFHelpers.LogHr("HW decoder SetInputType", sHr);
                    if (sHr >= 0) break;
                }
                finally
                {
                    Marshal.ReleaseComObject(template);
                }
            }

            for (int i = 0; i < 16; i++)
            {
                IMFMediaType template;
                int getHr = _decoder.GetOutputAvailableType(0, i, out template);
                if (getHr < 0)
                {
                    MFHelpers.Check(getHr, "HW decoder no NV12 output type");
                    return;
                }

                try
                {
                    var subKey = MFGuids.MF_MT_SUBTYPE;
                    Guid sub;
                    if (template.GetGUID(ref subKey, out sub) != 0) continue;
                    if (sub != MFGuids.MFVideoFormat_NV12) continue;

                    int sHr = _decoder.SetOutputType(0, template, 0);
                    MFHelpers.LogHr("HW decoder SetOutputType(NV12)", sHr);

                    if (sHr >= 0)
                    {
                        ulong sizePacked;
                        var sizeKey = MFGuids.MF_MT_FRAME_SIZE;
                        if (template.GetUINT64(ref sizeKey, out sizePacked) == 0)
                        {
                            _frameWidth = (int)(sizePacked >> 32);
                            _frameHeight = (int)(sizePacked & 0xFFFFFFFF);
                        }
                        return;
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(template);
                }
            }
        }

        private void ActivateConverter()
        {
            var clsid = MFGuids.CLSID_VideoProcessorMFT;
            var iid = new Guid("bf94c121-5b05-4e6f-8000-ba598961414d");
            object converterObj;
            int hr = MFNative.CoCreateInstance(ref clsid, IntPtr.Zero, MFConstants.CLSCTX_INPROC_SERVER, ref iid, out converterObj);
            MFHelpers.LogHr("Hw VP MFT CoCreateInstance", hr);
            MFHelpers.Check(hr, "VP MFT CoCreateInstance");
            _converter = (IMFTransform)converterObj;

            IMFAttributes attrs;
            if (_converter.GetAttributes(out attrs) == 0 && attrs != null)
            {
                try
                {
                    var unlockKey = MFGuids.MFT_TRANSFORM_ASYNC_UNLOCK;
                    int uHr = attrs.SetUINT32(ref unlockKey, 1);
                    MFHelpers.LogHr("VP MFT ASYNC_UNLOCK", uHr);
                }
                finally
                {
                    Marshal.ReleaseComObject(attrs);
                }
            }

            IntPtr mgrPtr = Marshal.GetIUnknownForObject(_dxgiManager);
            try
            {
                int dHr = _converter.ProcessMessage(MFConstants.MFT_MESSAGE_SET_D3D_MANAGER, mgrPtr);
                MFHelpers.LogHr("VP MFT SET_D3D_MANAGER", dHr);
            }
            finally
            {
                Marshal.Release(mgrPtr);
            }

            try
            {
                _converterEvents = _converter as IMFMediaEventGenerator;
                MFHelpers.Log("VP MFT events: " + (_converterEvents != null ? "yes" : "no"));
            }
            catch { }
        }

        private void ConfigureConverterTypes()
        {
            int w = _frameWidth, h = _frameHeight;
            MFHelpers.SetConverterTypeFromAvailable(_converter, isInput: true, subtype: MFGuids.MFVideoFormat_NV12, width: w, height: h, fps: 60, includeStride: false, label: "Hw VP input");
            MFHelpers.SetConverterTypeFromAvailable(_converter, isInput: false, subtype: MFGuids.MFVideoFormat_RGB32, width: w, height: h, fps: 60, includeStride: true, label: "Hw VP output");
        }

        private void StartStreaming()
        {
            MFHelpers.Check(_converter.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, IntPtr.Zero), "VP BEGIN_STREAMING");
            MFHelpers.Check(_converter.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_START_OF_STREAM, IntPtr.Zero), "VP START_OF_STREAM");
            MFHelpers.Check(_decoder.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, IntPtr.Zero), "HW decoder BEGIN_STREAMING");
            MFHelpers.Check(_decoder.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_START_OF_STREAM, IntPtr.Zero), "HW decoder START_OF_STREAM");
            _streaming = true;
            MFHelpers.Log("=== H264HwDecoder ready ===");
        }

        private void EnsureInputBuffer(int length)
        {
            if (_persistentInputBuffer != null && length <= _persistentInputCapacity) return;

            if (_persistentInputSample != null) { Marshal.ReleaseComObject(_persistentInputSample); _persistentInputSample = null; }
            if (_persistentInputBuffer != null) { Marshal.ReleaseComObject(_persistentInputBuffer); _persistentInputBuffer = null; }

            int capacity = Math.Max(length, 1024 * 1024);
            MFHelpers.Check(MFNative.MFCreateMemoryBuffer(capacity, out _persistentInputBuffer), "Hw MFCreateMemoryBuffer");
            MFHelpers.Check(MFNative.MFCreateSample(out _persistentInputSample), "Hw MFCreateSample");
            MFHelpers.Check(_persistentInputSample.AddBuffer(_persistentInputBuffer), "Hw input AddBuffer");
            _persistentInputCapacity = capacity;
        }

        private static void WaitForEvent(IMFMediaEventGenerator events, int expectedType, string label)
        {
            for (int i = 0; i < 200; i++)
            {
                IMFMediaEvent evt;
                int hr = events.GetEvent(0, out evt);
                if (hr < 0)
                {
                    MFHelpers.LogHr(label + " GetEvent", hr);
                    return;
                }

                int met = 0;
                try { evt.GetType(out met); }
                finally { Marshal.ReleaseComObject(evt); }

                if (met == expectedType) return;
            }
            MFHelpers.Log(label + " gave up after 200 events");
        }

        private static int TryGetEventNonBlocking(IMFMediaEventGenerator events)
        {
            const uint MF_EVENT_FLAG_NO_WAIT = 0x00000001;
            IMFMediaEvent evt;
            int hr = events.GetEvent(MF_EVENT_FLAG_NO_WAIT, out evt);
            if (hr < 0) return 0;
            int met = 0;
            try { evt.GetType(out met); }
            finally { Marshal.ReleaseComObject(evt); }
            return met;
        }

        private bool ProcessOutputDecoder(out IMFSample sample)
        {
            sample = null;
            var outputs = new MFT_OUTPUT_DATA_BUFFER[1];
            outputs[0].dwStreamID = 0;
            outputs[0].pSample = null;
            outputs[0].dwStatus = 0;
            outputs[0].pEvents = null;

            uint status;
            int hr = _decoder.ProcessOutput(0, 1, outputs, out status);
            if (hr == MFConstants.MF_E_TRANSFORM_NEED_MORE_INPUT) return false;
            if (hr == MFConstants.MF_E_TRANSFORM_STREAM_CHANGE)
            {
                IMFMediaType newType;
                if (_decoder.GetOutputAvailableType(0, 0, out newType) == 0)
                {
                    try { _decoder.SetOutputType(0, newType, 0); }
                    finally { Marshal.ReleaseComObject(newType); }
                }
                return false;
            }
            if (hr < 0) { MFHelpers.LogHr("HW decoder ProcessOutput", hr); return false; }

            sample = outputs[0].pSample;
            return sample != null;
        }

        private Bitmap ConvertToBitmap(IMFSample nv12Sample)
        {
            WaitForEvent(_converterEvents, MediaEventTypes.METransformNeedInput, "VP NeedInput");
            int hr = _converter.ProcessInput(0, nv12Sample, 0);
            if (hr < 0) { MFHelpers.LogHr("VP ProcessInput", hr); return null; }

            WaitForEvent(_converterEvents, MediaEventTypes.METransformHaveOutput, "VP HaveOutput");

            var outputs = new MFT_OUTPUT_DATA_BUFFER[1];
            outputs[0].dwStreamID = 0;
            outputs[0].pSample = null;
            outputs[0].dwStatus = 0;
            outputs[0].pEvents = null;

            uint status;
            int outHr = _converter.ProcessOutput(0, 1, outputs, out status);
            if (outHr < 0) { MFHelpers.LogHr("VP ProcessOutput", outHr); return null; }

            IMFSample bgraSample = outputs[0].pSample;
            if (bgraSample == null) return null;

            try
            {
                IMFMediaBuffer outBuffer;
                MFHelpers.Check(bgraSample.ConvertToContiguousBuffer(out outBuffer), "VP ConvertToContiguousBuffer");
                try
                {
                    IntPtr p;
                    int maxLen, curLen;
                    MFHelpers.Check(outBuffer.Lock(out p, out maxLen, out curLen), "VP out Lock");
                    try
                    {
                        int displayWidth = _expectedWidth > 0 ? Math.Min(_frameWidth, _expectedWidth) : _frameWidth;
                        int displayHeight = _expectedHeight > 0 ? Math.Min(_frameHeight, _expectedHeight) : _frameHeight;
                        if (displayWidth <= 0) displayWidth = _frameWidth;
                        if (displayHeight <= 0) displayHeight = _frameHeight;

                        var bitmap = _bitmapPool != null ? _bitmapPool.Take(displayWidth, displayHeight) : new Bitmap(displayWidth, displayHeight, PixelFormat.Format32bppRgb);
                        if (_bitmapPool != null) bitmap.Tag = _bitmapPool;
                        var rect = new Rectangle(0, 0, displayWidth, displayHeight);
                        var bits = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                        try
                        {
                            int srcStride = _frameWidth * 4;
                            int rowBytes = displayWidth * 4;
                            if (displayWidth == _frameWidth && bits.Stride == srcStride)
                            {
                                RtlMoveMemory(bits.Scan0, p, (UIntPtr)(srcStride * displayHeight));
                            }
                            else
                            {
                                for (int y = 0; y < displayHeight; y++)
                                {
                                    RtlMoveMemory(IntPtr.Add(bits.Scan0, y * bits.Stride), IntPtr.Add(p, y * srcStride), (UIntPtr)rowBytes);
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
                        MFHelpers.Check(outBuffer.Unlock(), "VP out Unlock");
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(outBuffer);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(bgraSample);
            }
        }

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory", SetLastError = false)]
        private static extern void RtlMoveMemory(IntPtr dest, IntPtr src, UIntPtr count);
    }
}
