using System;
using System.Runtime.InteropServices;
using ExtentDesktop.Shared;

namespace ExtentDesktop.Host
{
    internal sealed class H264HwEncoder : IDisposable
    {
        private readonly int _width;
        private readonly int _height;
        private readonly int _fps;
        private readonly long _frameDurationTicks;

        private IMFTransform _encoder;
        private IMFTransform _colorConverter;
        private IMFMediaEventGenerator _encoderEvents;
        private IMFDXGIDeviceManager _dxgiManager;
        private object _d3dDevice;

        private IMFMediaBuffer _bgraBuffer;
        private int _bgraBufferCapacity;
        private IMFSample _bgraSample;
        private IMFMediaBuffer _nv12Buffer;
        private int _nv12BufferCapacity;
        private IMFSample _nv12Sample;

        private long _frameIndex;
        private bool _streaming;
        private byte[] _outputScratch = new byte[256 * 1024];
        private bool _needInputPending;
        private bool _haveOutputPending;

        public H264HwEncoder(int width, int height, int fps, int bitrate)
        {
            if ((width & 1) != 0 || (height & 1) != 0)
                throw new ArgumentException("Width and height must be even.");

            _width = width;
            _height = height;
            _fps = fps;
            _frameDurationTicks = 10000000L / fps;

            MFHelpers.Check(MFNative.MFStartup(MFConstants.MF_VERSION, MFConstants.MFSTARTUP_LITE), "MFStartup");

            try
            {
                CreateD3DDevice();
                ActivateHwEncoder(bitrate);
                ActivateColorConverter();
                AllocateBuffers();
                StartStreaming();
            }
            catch
            {
                Cleanup();
                try { MFNative.MFShutdown(); } catch { }
                throw;
            }
        }

        private int _submitLogCount = 0;
        public void Submit(IntPtr bgraData, int bgraStride)
        {
            bool log = _submitLogCount < 3;
            if (log) MFHelpers.Log("HwEnc Submit#" + _submitLogCount + " begin");

            CopyBgraIntoBuffer(bgraData, bgraStride);

            long pts = _frameIndex * _frameDurationTicks;
            _frameIndex++;

            MFHelpers.Check(_bgraSample.SetSampleTime(pts), "Hw bgra SetSampleTime");
            MFHelpers.Check(_bgraSample.SetSampleDuration(_frameDurationTicks), "Hw bgra SetSampleDuration");

            if (log) MFHelpers.Log("HwEnc Submit#" + _submitLogCount + " CC.ProcessInput");
            int hr = _colorConverter.ProcessInput(0, _bgraSample, 0);
            MFHelpers.Check(hr, "Hw ColorConvert ProcessInput");

            if (log) MFHelpers.Log("HwEnc Submit#" + _submitLogCount + " before drain+feed");
            DrainColorConverterAndFeedEncoder(pts);
            if (log) MFHelpers.Log("HwEnc Submit#" + _submitLogCount + " done");
            _submitLogCount++;
        }

        private int _drainSuccessLogged = 0;
        public bool TryDrainOutput(out byte[] buffer, out int length, out bool isKeyframe)
        {
            buffer = null;
            length = 0;
            isKeyframe = false;

            DrainEvents();
            if (!_haveOutputPending) return false;
            _haveOutputPending = false;

            bool ok = DoEncoderProcessOutput(out buffer, out length, out isKeyframe);
            if (ok && _drainSuccessLogged < 5)
            {
                _drainSuccessLogged++;
                MFHelpers.Log("HwEnc drain ok bytes=" + length + " key=" + isKeyframe);
            }
            return ok;
        }

        private void DrainEvents()
        {
            const uint MF_EVENT_FLAG_NO_WAIT = 0x00000001;
            while (true)
            {
                IMFMediaEvent evt;
                int hr = _encoderEvents.GetEvent(MF_EVENT_FLAG_NO_WAIT, out evt);
                if (hr < 0) return;
                int met = 0;
                try { evt.GetType(out met); }
                finally { Marshal.ReleaseComObject(evt); }
                if (met == MediaEventTypes.METransformNeedInput) _needInputPending = true;
                else if (met == MediaEventTypes.METransformHaveOutput) _haveOutputPending = true;
            }
        }

        private bool WaitForNeedInput(int maxIters)
        {
            DrainEvents();
            while (!_needInputPending && maxIters-- > 0)
            {
                IMFMediaEvent evt;
                int hr = _encoderEvents.GetEvent(0, out evt);
                if (hr < 0) return false;
                int met = 0;
                try { evt.GetType(out met); }
                finally { Marshal.ReleaseComObject(evt); }
                if (met == MediaEventTypes.METransformNeedInput) _needInputPending = true;
                else if (met == MediaEventTypes.METransformHaveOutput) _haveOutputPending = true;
            }
            return _needInputPending;
        }

        public void Dispose()
        {
            try
            {
                if (_streaming)
                {
                    if (_encoder != null)
                    {
                        _encoder.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_END_OF_STREAM, IntPtr.Zero);
                        _encoder.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_END_STREAMING, IntPtr.Zero);
                    }
                    if (_colorConverter != null)
                    {
                        _colorConverter.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_END_OF_STREAM, IntPtr.Zero);
                        _colorConverter.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_END_STREAMING, IntPtr.Zero);
                    }
                }
            }
            catch { }
            Cleanup();
            try { MFNative.MFShutdown(); } catch { }
        }

        private void Cleanup()
        {
            if (_bgraSample != null) { try { Marshal.ReleaseComObject(_bgraSample); } catch { } _bgraSample = null; }
            if (_bgraBuffer != null) { try { Marshal.ReleaseComObject(_bgraBuffer); } catch { } _bgraBuffer = null; }
            if (_nv12Sample != null) { try { Marshal.ReleaseComObject(_nv12Sample); } catch { } _nv12Sample = null; }
            if (_nv12Buffer != null) { try { Marshal.ReleaseComObject(_nv12Buffer); } catch { } _nv12Buffer = null; }
            if (_encoderEvents != null) { try { Marshal.ReleaseComObject(_encoderEvents); } catch { } _encoderEvents = null; }
            if (_colorConverter != null) { try { Marshal.ReleaseComObject(_colorConverter); } catch { } _colorConverter = null; }
            if (_encoder != null) { try { Marshal.ReleaseComObject(_encoder); } catch { } _encoder = null; }
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
            MFHelpers.LogHr("HwEnc D3D11CreateDevice", hr);
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

        private void ActivateHwEncoder(int bitrate)
        {
            var outputType = new MFT_REGISTER_TYPE_INFO
            {
                guidMajorType = MFGuids.MFMediaType_Video,
                guidSubtype = MFGuids.MFVideoFormat_H264
            };

            IntPtr outputTypePtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(MFT_REGISTER_TYPE_INFO)));
            IntPtr arrayPtr;
            int count;
            try
            {
                Marshal.StructureToPtr(outputType, outputTypePtr, false);
                int hr = MFNative.MFTEnumEx(
                    MFGuids.MFT_CATEGORY_VIDEO_ENCODER,
                    (int)(MFT_ENUM_FLAG.Hardware | MFT_ENUM_FLAG.SortAndFilter),
                    IntPtr.Zero,
                    outputTypePtr,
                    out arrayPtr, out count);
                MFHelpers.Log("HwEnc MFTEnumEx hr=0x" + hr.ToString("X8") + " count=" + count);
                MFHelpers.Check(hr, "HwEnc MFTEnumEx");
                if (count == 0)
                {
                    if (arrayPtr != IntPtr.Zero) MFNative.CoTaskMemFree(arrayPtr);
                    throw new InvalidOperationException("No HW H.264 encoder found");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(outputTypePtr);
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
                            MFHelpers.Log("Activating HW encoder: " + sb.ToString());
                    }

                    var iid = new Guid("bf94c121-5b05-4e6f-8000-ba598961414d");
                    object encoderObj;
                    int hr = activate.ActivateObject(ref iid, out encoderObj);
                    MFHelpers.Check(hr, "ActivateObject(HW encoder)");
                    _encoder = (IMFTransform)encoderObj;
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
            MFHelpers.Check(_encoder.GetAttributes(out attrs), "HW encoder GetAttributes");
            try
            {
                var unlockKey = MFGuids.MFT_TRANSFORM_ASYNC_UNLOCK;
                MFHelpers.Check(attrs.SetUINT32(ref unlockKey, 1), "HW encoder ASYNC_UNLOCK");
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
                MFHelpers.Check(_encoder.ProcessMessage(MFConstants.MFT_MESSAGE_SET_D3D_MANAGER, mgrPtr), "HW encoder SET_D3D_MANAGER");
            }
            finally
            {
                Marshal.Release(mgrPtr);
            }

            ConfigureEncoderTypes(bitrate);

            _encoderEvents = (IMFMediaEventGenerator)_encoder;
        }

        private void ConfigureEncoderTypes(int bitrate)
        {
            for (int i = 0; ; i++)
            {
                IMFMediaType template;
                int getHr = _encoder.GetOutputAvailableType(0, i, out template);
                if (getHr < 0) { MFHelpers.Check(getHr, "HW enc no output template"); return; }

                try
                {
                    var subKey = MFGuids.MF_MT_SUBTYPE;
                    Guid sub;
                    if (template.GetGUID(ref subKey, out sub) != 0) continue;
                    if (sub != MFGuids.MFVideoFormat_H264) continue;

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

                    int sHr = _encoder.SetOutputType(0, template, 0);
                    MFHelpers.LogHr("HW enc SetOutputType", sHr);
                    if (sHr >= 0) break;
                }
                finally
                {
                    Marshal.ReleaseComObject(template);
                }
            }

            for (int i = 0; ; i++)
            {
                IMFMediaType template;
                int getHr = _encoder.GetInputAvailableType(0, i, out template);
                if (getHr < 0) { MFHelpers.Check(getHr, "HW enc no input template"); return; }

                try
                {
                    var subKey = MFGuids.MF_MT_SUBTYPE;
                    Guid sub;
                    if (template.GetGUID(ref subKey, out sub) != 0) continue;
                    if (sub != MFGuids.MFVideoFormat_NV12) continue;

                    var sizeKey = MFGuids.MF_MT_FRAME_SIZE;
                    template.SetUINT64(ref sizeKey, MFHelpers.PackUInt64((uint)_width, (uint)_height));
                    var rateKey = MFGuids.MF_MT_FRAME_RATE;
                    template.SetUINT64(ref rateKey, MFHelpers.PackUInt64((uint)_fps, 1));
                    var aspectKey = MFGuids.MF_MT_PIXEL_ASPECT_RATIO;
                    template.SetUINT64(ref aspectKey, MFHelpers.PackUInt64(1, 1));
                    var interlaceKey = MFGuids.MF_MT_INTERLACE_MODE;
                    template.SetUINT32(ref interlaceKey, (uint)MFConstants.MFVideoInterlace_Progressive);

                    int sHr = _encoder.SetInputType(0, template, 0);
                    MFHelpers.LogHr("HW enc SetInputType", sHr);
                    if (sHr >= 0) return;
                }
                finally
                {
                    Marshal.ReleaseComObject(template);
                }
            }
        }

        private void ActivateColorConverter()
        {
            var clsid = MFGuids.CLSID_CColorConvertDMO;
            var iid = new Guid("bf94c121-5b05-4e6f-8000-ba598961414d");
            object obj;
            int hr = MFNative.CoCreateInstance(ref clsid, IntPtr.Zero, MFConstants.CLSCTX_INPROC_SERVER, ref iid, out obj);
            MFHelpers.LogHr("HwEnc ColorConvertDMO", hr);
            MFHelpers.Check(hr, "ColorConvertDMO");
            _colorConverter = (IMFTransform)obj;

            MFHelpers.SetConverterTypeFromAvailable(_colorConverter, isInput: true, subtype: MFGuids.MFVideoFormat_RGB32, width: _width, height: _height, fps: _fps, includeStride: true, label: "HwEnc CC input");
            MFHelpers.SetConverterTypeFromAvailable(_colorConverter, isInput: false, subtype: MFGuids.MFVideoFormat_NV12, width: _width, height: _height, fps: _fps, includeStride: false, label: "HwEnc CC output");
        }

        private void AllocateBuffers()
        {
            _bgraBufferCapacity = _width * 4 * _height;
            MFHelpers.Check(MFNative.MFCreateMemoryBuffer(_bgraBufferCapacity, out _bgraBuffer), "HwEnc MFCreateMemoryBuffer(bgra)");
            MFHelpers.Check(MFNative.MFCreateSample(out _bgraSample), "HwEnc MFCreateSample(bgra)");
            MFHelpers.Check(_bgraSample.AddBuffer(_bgraBuffer), "HwEnc bgra AddBuffer");

            _nv12BufferCapacity = PixelConvert.Nv12Size(_width, _height);
            MFHelpers.Check(MFNative.MFCreateMemoryBuffer(_nv12BufferCapacity, out _nv12Buffer), "HwEnc MFCreateMemoryBuffer(nv12)");
            MFHelpers.Check(MFNative.MFCreateSample(out _nv12Sample), "HwEnc MFCreateSample(nv12)");
            MFHelpers.Check(_nv12Sample.AddBuffer(_nv12Buffer), "HwEnc nv12 AddBuffer");
        }

        private void StartStreaming()
        {
            MFHelpers.Check(_colorConverter.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, IntPtr.Zero), "HwEnc CC BEGIN_STREAMING");
            MFHelpers.Check(_colorConverter.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_START_OF_STREAM, IntPtr.Zero), "HwEnc CC START_OF_STREAM");
            MFHelpers.Check(_encoder.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, IntPtr.Zero), "HW encoder BEGIN_STREAMING");
            MFHelpers.Check(_encoder.ProcessMessage(MFConstants.MFT_MESSAGE_NOTIFY_START_OF_STREAM, IntPtr.Zero), "HW encoder START_OF_STREAM");
            _streaming = true;
            MFHelpers.Log("=== H264HwEncoder ready ===");
        }

        private void CopyBgraIntoBuffer(IntPtr bgraData, int bgraStride)
        {
            IntPtr p;
            int maxLen, curLen;
            MFHelpers.Check(_bgraBuffer.Lock(out p, out maxLen, out curLen), "Hw bgra Lock");
            try
            {
                int dstStride = _width * 4;
                if (bgraStride == dstStride)
                {
                    RtlMoveMemory(p, bgraData, (UIntPtr)_bgraBufferCapacity);
                }
                else
                {
                    for (int y = 0; y < _height; y++)
                    {
                        RtlMoveMemory(IntPtr.Add(p, y * dstStride), IntPtr.Add(bgraData, y * bgraStride), (UIntPtr)dstStride);
                    }
                }
            }
            finally
            {
                MFHelpers.Check(_bgraBuffer.Unlock(), "Hw bgra Unlock");
            }
            MFHelpers.Check(_bgraBuffer.SetCurrentLength(_bgraBufferCapacity), "Hw bgra SetCurrentLength");
        }

        private void DrainColorConverterAndFeedEncoder(long pts)
        {
            MFHelpers.Check(_nv12Buffer.SetCurrentLength(0), "Hw nv12 SetCurrentLength(0)");

            var outputs = new MFT_OUTPUT_DATA_BUFFER[1];
            outputs[0].dwStreamID = 0;
            outputs[0].pSample = _nv12Sample;
            outputs[0].dwStatus = 0;
            outputs[0].pEvents = null;

            uint status;
            int hr = _colorConverter.ProcessOutput(0, 1, outputs, out status);
            if (hr == MFConstants.MF_E_TRANSFORM_NEED_MORE_INPUT) return;
            MFHelpers.Check(hr, "Hw CC ProcessOutput");

            MFHelpers.Check(_nv12Sample.SetSampleTime(pts), "Hw nv12 SetSampleTime");
            MFHelpers.Check(_nv12Sample.SetSampleDuration(_frameDurationTicks), "Hw nv12 SetSampleDuration");

            bool subLog = _submitLogCount < 3;
            if (subLog) MFHelpers.Log("HwEnc waiting for NeedInput");
            if (!WaitForNeedInput(200))
            {
                MFHelpers.Log("HwEnc NeedInput timeout");
                return;
            }
            _needInputPending = false;
            if (subLog) MFHelpers.Log("HwEnc got NeedInput, calling ProcessInput");

            int piHr = _encoder.ProcessInput(0, _nv12Sample, 0);
            if (subLog) MFHelpers.Log("HwEnc ProcessInput hr=0x" + piHr.ToString("X8"));
            MFHelpers.Check(piHr, "HW encoder ProcessInput");
        }

        private bool DoEncoderProcessOutput(out byte[] buffer, out int length, out bool isKeyframe)
        {
            buffer = null;
            length = 0;
            isKeyframe = false;

            var outputs = new MFT_OUTPUT_DATA_BUFFER[1];
            outputs[0].dwStreamID = 0;
            outputs[0].pSample = null;
            outputs[0].dwStatus = 0;
            outputs[0].pEvents = null;

            uint status;
            int hr = _encoder.ProcessOutput(0, 1, outputs, out status);
            if (hr == MFConstants.MF_E_TRANSFORM_NEED_MORE_INPUT) return false;
            if (hr < 0) { MFHelpers.LogHr("HW encoder ProcessOutput", hr); return false; }

            IMFSample sample = outputs[0].pSample;
            if (sample == null) return false;

            try
            {
                IMFMediaBuffer mediaBuffer;
                MFHelpers.Check(sample.ConvertToContiguousBuffer(out mediaBuffer), "HwEnc out ConvertToContiguousBuffer");
                try
                {
                    IntPtr p;
                    int maxLen, curLen;
                    MFHelpers.Check(mediaBuffer.Lock(out p, out maxLen, out curLen), "HwEnc out Lock");
                    try
                    {
                        if (curLen > 0)
                        {
                            if (curLen > _outputScratch.Length)
                                _outputScratch = new byte[Math.Max(curLen, _outputScratch.Length * 2)];
                            Marshal.Copy(p, _outputScratch, 0, curLen);
                            buffer = _outputScratch;
                            length = curLen;
                        }
                    }
                    finally
                    {
                        MFHelpers.Check(mediaBuffer.Unlock(), "HwEnc out Unlock");
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(mediaBuffer);
                }

                int sampleFlags;
                if (sample.GetSampleFlags(out sampleFlags) == 0)
                    isKeyframe = (sampleFlags & 0x1) != 0;
            }
            finally
            {
                Marshal.ReleaseComObject(sample);
            }

            return length > 0;
        }

        private static void WaitForEvent(IMFMediaEventGenerator events, int expectedType, string label)
        {
            for (int i = 0; i < 200; i++)
            {
                IMFMediaEvent evt;
                int hr = events.GetEvent(0, out evt);
                if (hr < 0) return;
                int met = 0;
                try { evt.GetType(out met); }
                finally { Marshal.ReleaseComObject(evt); }
                if (met == expectedType) return;
            }
            MFHelpers.Log(label + " timeout");
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

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory", SetLastError = false)]
        private static extern void RtlMoveMemory(IntPtr dest, IntPtr src, UIntPtr count);
    }
}
