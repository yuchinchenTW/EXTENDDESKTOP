using System;
using System.Runtime.InteropServices;

namespace ExtentDesktop.Shared
{
    internal static class MFGuids
    {
        public static readonly Guid MFMediaType_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFVideoFormat_H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFVideoFormat_NV12 = new Guid("3231564E-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFVideoFormat_RGB32 = new Guid("00000016-0000-0010-8000-00AA00389B71");
        public static readonly Guid CLSID_CColorConvertDMO = new Guid("98230571-0087-4204-b020-3282538e57d3");
        public static readonly Guid CLSID_VideoProcessorMFT = new Guid("88753b26-5b24-49bd-b2e7-0c445c78c982");

        public static readonly Guid MF_MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a5f");
        public static readonly Guid MF_MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static readonly Guid MF_MT_FRAME_SIZE = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        public static readonly Guid MF_MT_FRAME_RATE = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        public static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        public static readonly Guid MF_MT_INTERLACE_MODE = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        public static readonly Guid MF_MT_AVG_BITRATE = new Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        public static readonly Guid MF_MT_MPEG2_PROFILE = new Guid("ad76a80b-2d5c-4e0b-b375-64e520137036");
        public static readonly Guid MF_MT_MPEG2_LEVEL = new Guid("96f66574-11c5-4015-8666-bff516436da7");
        public static readonly Guid MF_MT_DEFAULT_STRIDE = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        public static readonly Guid MF_MT_ALL_SAMPLES_INDEPENDENT = new Guid("c9173739-5e56-461c-b713-46fb995cb95f");
        public static readonly Guid MF_LOW_LATENCY = new Guid("9c27891a-ed7a-40e1-88e8-b22727a024ee");

        public static readonly Guid CLSID_CMSH264EncoderMFT = new Guid("6CA50344-051A-4DED-9779-A43305165E35");
        public static readonly Guid CLSID_CMSH264DecoderMFT = new Guid("62CE7E72-4C71-4D20-B15D-452831A87D9D");

        public static readonly Guid CODECAPI_AVLowLatencyMode = new Guid("9C27891A-ED7A-40e1-88E8-B22727A024EE");
        public static readonly Guid CODECAPI_AVEncCommonRateControlMode = new Guid("1C0608E9-370C-4710-8A58-CB6181C42423");
        public static readonly Guid CODECAPI_AVEncCommonMeanBitRate = new Guid("F7222374-2144-4815-B550-A37F8E12EE52");
        public static readonly Guid CODECAPI_AVEncMPVGOPSize = new Guid("95F31B26-95A4-4DA1-AE8B-7595A09EB2EE");
        public static readonly Guid CODECAPI_AVEncMPVDefaultBPictureCount = new Guid("8d390aac-dc5c-4200-b57f-814d04bafab2");
        public static readonly Guid CODECAPI_AVEncCommonQuality = new Guid("fcbf57a3-7ea5-4b0c-9644-69b40c39c391");
        public static readonly Guid CODECAPI_AVEncNumWorkerThreads = new Guid("e215aebe-9c83-426c-95e3-6f0395a97c52");

        public static readonly Guid IID_ICodecAPI = new Guid("901db4c7-31ce-41a2-85dc-8fa0bf41b8da");

        public static readonly Guid MFT_TRANSFORM_ASYNC = new Guid("f81a699a-649a-497d-8c73-29f8fed6ad7a");
        public static readonly Guid MFT_TRANSFORM_ASYNC_UNLOCK = new Guid("e5666d6b-3422-4eb6-a421-da7db1f8e207");

        public static readonly Guid MFT_CATEGORY_VIDEO_ENCODER = new Guid("f79eac7d-e545-4387-bdee-d647d7bde42a");
        public static readonly Guid MFT_CATEGORY_VIDEO_DECODER = new Guid("d6c02d4b-6833-45b4-971a-05a4b04bab91");
        public static readonly Guid MFT_FRIENDLY_NAME_Attribute = new Guid("314ffbae-5b41-4c95-9c19-4e7d586face3");
    }

    internal static class MFConstants
    {
        public const int MF_VERSION = 0x00020070;
        public const int MFSTARTUP_FULL = 0;
        public const int MFSTARTUP_LITE = 1;

        public const int MFVideoInterlace_Progressive = 2;

        public const int eAVEncH264VProfile_Base = 66;
        public const int eAVEncH264VProfile_Main = 77;
        public const int eAVEncH264VProfile_High = 100;

        public const int eAVEncCommonRateControlMode_CBR = 0;
        public const int eAVEncCommonRateControlMode_PeakConstrainedVBR = 1;
        public const int eAVEncCommonRateControlMode_UnconstrainedVBR = 2;
        public const int eAVEncCommonRateControlMode_Quality = 3;

        public const int MFT_MESSAGE_SET_D3D_MANAGER = 2;
        public const int MFT_MESSAGE_NOTIFY_BEGIN_STREAMING = 0x10000000;
        public const int MFT_MESSAGE_NOTIFY_END_STREAMING = 0x10000001;
        public const int MFT_MESSAGE_NOTIFY_END_OF_STREAM = 0x10000002;
        public const int MFT_MESSAGE_NOTIFY_START_OF_STREAM = 0x10000003;
        public const int MFT_MESSAGE_COMMAND_FLUSH = 0x00000000;
        public const int MFT_MESSAGE_COMMAND_DRAIN = 0x00000001;

        public const int MFT_OUTPUT_STATUS_SAMPLE_READY = 0x00000001;

        public const uint MFT_OUTPUT_DATA_BUFFER_INCOMPLETE = 0x01000000;
        public const uint MFT_OUTPUT_DATA_BUFFER_FORMAT_CHANGE = 0x00000100;
        public const uint MFT_OUTPUT_DATA_BUFFER_STREAM_END = 0x00000200;

        public const int MFT_OUTPUT_STREAM_PROVIDES_SAMPLES = 0x00000100;
        public const int MFT_OUTPUT_STREAM_CAN_PROVIDE_SAMPLES = 0x00000200;

        public const int MF_E_TRANSFORM_NEED_MORE_INPUT = unchecked((int)0xC00D6D72);
        public const int MF_E_TRANSFORM_STREAM_CHANGE = unchecked((int)0xC00D6D61);
        public const int MF_E_INVALIDMEDIATYPE = unchecked((int)0xC00D36B4);

        public const int CLSCTX_INPROC_SERVER = 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MFT_OUTPUT_STREAM_INFO
    {
        public int dwFlags;
        public int cbSize;
        public int cbAlignment;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MFT_INPUT_STREAM_INFO
    {
        public long hnsMaxLatency;
        public int dwFlags;
        public int cbSize;
        public int cbMaxLookahead;
        public int cbAlignment;
    }

    [Flags]
    internal enum MFT_ENUM_FLAG : uint
    {
        SyncMFT = 0x00000001,
        AsyncMFT = 0x00000002,
        Hardware = 0x00000004,
        FieldOfUse = 0x00000008,
        LocalMFT = 0x00000010,
        TranscodeOnly = 0x00000020,
        SortAndFilter = 0x00000040,
        All = 0x0000003F
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MFT_REGISTER_TYPE_INFO
    {
        public Guid guidMajorType;
        public Guid guidSubtype;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MFT_OUTPUT_DATA_BUFFER
    {
        public int dwStreamID;
        [MarshalAs(UnmanagedType.Interface)] public IMFSample pSample;
        public uint dwStatus;
        [MarshalAs(UnmanagedType.Interface)] public IMFCollection pEvents;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPVARIANT_BOOL
    {
        public ushort vt;
        public ushort r1;
        public ushort r2;
        public ushort r3;
        public int boolVal;
        public int padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPVARIANT_UI4
    {
        public ushort vt;
        public ushort r1;
        public ushort r2;
        public ushort r3;
        public uint ulVal;
        public int padding;
    }

    [ComImport, Guid("00000000-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IUnknown
    {
    }

    [ComImport, Guid("5dfd7a6a-f422-4242-9d39-2d11d8b6f0ac"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFCollection
    {
        [PreserveSig] int GetElementCount(out int pcElements);
        [PreserveSig] int GetElement(int dwElementIndex, [MarshalAs(UnmanagedType.IUnknown)] out object ppUnkElement);
        [PreserveSig] int AddElement([MarshalAs(UnmanagedType.IUnknown)] object pUnkElement);
        [PreserveSig] int RemoveElement(int dwElementIndex, [MarshalAs(UnmanagedType.IUnknown)] out object ppUnkElement);
        [PreserveSig] int InsertElementAt(int dwIndex, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] int RemoveAllElements();
    }

    [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFAttributes
    {
        [PreserveSig] int GetItem(ref Guid guidKey, IntPtr pValue);
        [PreserveSig] int GetItemType(ref Guid guidKey, out int pType);
        [PreserveSig] int CompareItem(ref Guid guidKey, IntPtr Value, out bool pbResult);
        [PreserveSig] int Compare(IMFAttributes pTheirs, int MatchType, out bool pbResult);
        [PreserveSig] int GetUINT32(ref Guid guidKey, out uint punValue);
        [PreserveSig] int GetUINT64(ref Guid guidKey, out ulong punValue);
        [PreserveSig] int GetDouble(ref Guid guidKey, out double pfValue);
        [PreserveSig] int GetGUID(ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] int GetStringLength(ref Guid guidKey, out int pcchLength);
        [PreserveSig] int GetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszValue, int cchBufSize, out int pcchLength);
        [PreserveSig] int GetAllocatedString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out int pcchLength);
        [PreserveSig] int GetBlobSize(ref Guid guidKey, out int pcbBlobSize);
        [PreserveSig] int GetBlob(ref Guid guidKey, [Out] byte[] pBuf, int cbBufSize, out int pcbBlobSize);
        [PreserveSig] int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out int pcbSize);
        [PreserveSig] int GetUnknown(ref Guid guidKey, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [PreserveSig] int SetItem(ref Guid guidKey, IntPtr Value);
        [PreserveSig] int DeleteItem(ref Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid guidKey, uint unValue);
        [PreserveSig] int SetUINT64(ref Guid guidKey, ulong unValue);
        [PreserveSig] int SetDouble(ref Guid guidKey, double fValue);
        [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue);
        [PreserveSig] int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] int SetBlob(ref Guid guidKey, byte[] pBuf, int cbBufSize);
        [PreserveSig] int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out int pcItems);
        [PreserveSig] int GetItemByIndex(int unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] int CopyAllItems(IMFAttributes pDest);
    }

    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFMediaType : IMFAttributes
    {
        [PreserveSig] new int GetItem(ref Guid guidKey, IntPtr pValue);
        [PreserveSig] new int GetItemType(ref Guid guidKey, out int pType);
        [PreserveSig] new int CompareItem(ref Guid guidKey, IntPtr Value, out bool pbResult);
        [PreserveSig] new int Compare(IMFAttributes pTheirs, int MatchType, out bool pbResult);
        [PreserveSig] new int GetUINT32(ref Guid guidKey, out uint punValue);
        [PreserveSig] new int GetUINT64(ref Guid guidKey, out ulong punValue);
        [PreserveSig] new int GetDouble(ref Guid guidKey, out double pfValue);
        [PreserveSig] new int GetGUID(ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] new int GetStringLength(ref Guid guidKey, out int pcchLength);
        [PreserveSig] new int GetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszValue, int cchBufSize, out int pcchLength);
        [PreserveSig] new int GetAllocatedString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out int pcchLength);
        [PreserveSig] new int GetBlobSize(ref Guid guidKey, out int pcbBlobSize);
        [PreserveSig] new int GetBlob(ref Guid guidKey, [Out] byte[] pBuf, int cbBufSize, out int pcbBlobSize);
        [PreserveSig] new int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out int pcbSize);
        [PreserveSig] new int GetUnknown(ref Guid guidKey, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [PreserveSig] new int SetItem(ref Guid guidKey, IntPtr Value);
        [PreserveSig] new int DeleteItem(ref Guid guidKey);
        [PreserveSig] new int DeleteAllItems();
        [PreserveSig] new int SetUINT32(ref Guid guidKey, uint unValue);
        [PreserveSig] new int SetUINT64(ref Guid guidKey, ulong unValue);
        [PreserveSig] new int SetDouble(ref Guid guidKey, double fValue);
        [PreserveSig] new int SetGUID(ref Guid guidKey, ref Guid guidValue);
        [PreserveSig] new int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] new int SetBlob(ref Guid guidKey, byte[] pBuf, int cbBufSize);
        [PreserveSig] new int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] new int LockStore();
        [PreserveSig] new int UnlockStore();
        [PreserveSig] new int GetCount(out int pcItems);
        [PreserveSig] new int GetItemByIndex(int unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] new int CopyAllItems(IMFAttributes pDest);

        [PreserveSig] int GetMajorType(out Guid pguidMajorType);
        [PreserveSig] int IsCompressedFormat(out bool pfCompressed);
        [PreserveSig] int IsEqual(IMFMediaType pIMediaType, out int pdwFlags);
        [PreserveSig] int GetRepresentation(Guid guidRepresentation, out IntPtr ppvRepresentation);
        [PreserveSig] int FreeRepresentation(Guid guidRepresentation, IntPtr pvRepresentation);
    }

    [ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr ppbBuffer, out int pcbMaxLength, out int pcbCurrentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out int pcbCurrentLength);
        [PreserveSig] int SetCurrentLength(int cbCurrentLength);
        [PreserveSig] int GetMaxLength(out int pcbMaxLength);
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFSample : IMFAttributes
    {
        [PreserveSig] new int GetItem(ref Guid guidKey, IntPtr pValue);
        [PreserveSig] new int GetItemType(ref Guid guidKey, out int pType);
        [PreserveSig] new int CompareItem(ref Guid guidKey, IntPtr Value, out bool pbResult);
        [PreserveSig] new int Compare(IMFAttributes pTheirs, int MatchType, out bool pbResult);
        [PreserveSig] new int GetUINT32(ref Guid guidKey, out uint punValue);
        [PreserveSig] new int GetUINT64(ref Guid guidKey, out ulong punValue);
        [PreserveSig] new int GetDouble(ref Guid guidKey, out double pfValue);
        [PreserveSig] new int GetGUID(ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] new int GetStringLength(ref Guid guidKey, out int pcchLength);
        [PreserveSig] new int GetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszValue, int cchBufSize, out int pcchLength);
        [PreserveSig] new int GetAllocatedString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out int pcchLength);
        [PreserveSig] new int GetBlobSize(ref Guid guidKey, out int pcbBlobSize);
        [PreserveSig] new int GetBlob(ref Guid guidKey, [Out] byte[] pBuf, int cbBufSize, out int pcbBlobSize);
        [PreserveSig] new int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out int pcbSize);
        [PreserveSig] new int GetUnknown(ref Guid guidKey, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [PreserveSig] new int SetItem(ref Guid guidKey, IntPtr Value);
        [PreserveSig] new int DeleteItem(ref Guid guidKey);
        [PreserveSig] new int DeleteAllItems();
        [PreserveSig] new int SetUINT32(ref Guid guidKey, uint unValue);
        [PreserveSig] new int SetUINT64(ref Guid guidKey, ulong unValue);
        [PreserveSig] new int SetDouble(ref Guid guidKey, double fValue);
        [PreserveSig] new int SetGUID(ref Guid guidKey, ref Guid guidValue);
        [PreserveSig] new int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] new int SetBlob(ref Guid guidKey, byte[] pBuf, int cbBufSize);
        [PreserveSig] new int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] new int LockStore();
        [PreserveSig] new int UnlockStore();
        [PreserveSig] new int GetCount(out int pcItems);
        [PreserveSig] new int GetItemByIndex(int unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] new int CopyAllItems(IMFAttributes pDest);

        [PreserveSig] int GetSampleFlags(out int pdwSampleFlags);
        [PreserveSig] int SetSampleFlags(int dwSampleFlags);
        [PreserveSig] int GetSampleTime(out long phnsSampleTime);
        [PreserveSig] int SetSampleTime(long hnsSampleTime);
        [PreserveSig] int GetSampleDuration(out long phnsSampleDuration);
        [PreserveSig] int SetSampleDuration(long hnsSampleDuration);
        [PreserveSig] int GetBufferCount(out int pdwBufferCount);
        [PreserveSig] int GetBufferByIndex(int dwIndex, out IMFMediaBuffer ppBuffer);
        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);
        [PreserveSig] int AddBuffer(IMFMediaBuffer pBuffer);
        [PreserveSig] int RemoveBufferByIndex(int dwIndex);
        [PreserveSig] int RemoveAllBuffers();
        [PreserveSig] int GetTotalLength(out int pcbTotalLength);
        [PreserveSig] int CopyToBuffer(IMFMediaBuffer pBuffer);
    }

    [ComImport, Guid("eb533d5d-2db6-40f8-97a9-494692014f07"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFDXGIDeviceManager
    {
        [PreserveSig] int CloseDeviceHandle(IntPtr hDevice);
        [PreserveSig] int GetVideoService(IntPtr hDevice, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppService);
        [PreserveSig] int LockDevice(IntPtr hDevice, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppUnkDevice, [MarshalAs(UnmanagedType.Bool)] bool fBlock);
        [PreserveSig] int OpenDeviceHandle(out IntPtr phDevice);
        [PreserveSig] int ResetDevice([MarshalAs(UnmanagedType.IUnknown)] object pUnkDevice, int resetToken);
        [PreserveSig] int TestDevice(IntPtr hDevice);
        [PreserveSig] int UnlockDevice(IntPtr hDevice, [MarshalAs(UnmanagedType.Bool)] bool fSaveState);
    }

    [ComImport, Guid("9B7E4C8F-342C-4106-A19F-4F2704F689F0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ID3D10Multithread
    {
        [PreserveSig] void Enter();
        [PreserveSig] void Leave();
        [PreserveSig]
        [return: MarshalAs(UnmanagedType.Bool)]
        bool SetMultithreadProtected([MarshalAs(UnmanagedType.Bool)] bool bMTProtect);
        [PreserveSig]
        [return: MarshalAs(UnmanagedType.Bool)]
        bool GetMultithreadProtected();
    }

    [ComImport, Guid("7FEE9E9A-4A89-47a6-899C-B6A53A70FB67"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFActivate : IMFAttributes
    {
        [PreserveSig] new int GetItem(ref Guid guidKey, IntPtr pValue);
        [PreserveSig] new int GetItemType(ref Guid guidKey, out int pType);
        [PreserveSig] new int CompareItem(ref Guid guidKey, IntPtr Value, out bool pbResult);
        [PreserveSig] new int Compare(IMFAttributes pTheirs, int MatchType, out bool pbResult);
        [PreserveSig] new int GetUINT32(ref Guid guidKey, out uint punValue);
        [PreserveSig] new int GetUINT64(ref Guid guidKey, out ulong punValue);
        [PreserveSig] new int GetDouble(ref Guid guidKey, out double pfValue);
        [PreserveSig] new int GetGUID(ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] new int GetStringLength(ref Guid guidKey, out int pcchLength);
        [PreserveSig] new int GetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszValue, int cchBufSize, out int pcchLength);
        [PreserveSig] new int GetAllocatedString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out int pcchLength);
        [PreserveSig] new int GetBlobSize(ref Guid guidKey, out int pcbBlobSize);
        [PreserveSig] new int GetBlob(ref Guid guidKey, [Out] byte[] pBuf, int cbBufSize, out int pcbBlobSize);
        [PreserveSig] new int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out int pcbSize);
        [PreserveSig] new int GetUnknown(ref Guid guidKey, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [PreserveSig] new int SetItem(ref Guid guidKey, IntPtr Value);
        [PreserveSig] new int DeleteItem(ref Guid guidKey);
        [PreserveSig] new int DeleteAllItems();
        [PreserveSig] new int SetUINT32(ref Guid guidKey, uint unValue);
        [PreserveSig] new int SetUINT64(ref Guid guidKey, ulong unValue);
        [PreserveSig] new int SetDouble(ref Guid guidKey, double fValue);
        [PreserveSig] new int SetGUID(ref Guid guidKey, ref Guid guidValue);
        [PreserveSig] new int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] new int SetBlob(ref Guid guidKey, byte[] pBuf, int cbBufSize);
        [PreserveSig] new int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] new int LockStore();
        [PreserveSig] new int UnlockStore();
        [PreserveSig] new int GetCount(out int pcItems);
        [PreserveSig] new int GetItemByIndex(int unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] new int CopyAllItems(IMFAttributes pDest);

        [PreserveSig] int ActivateObject(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [PreserveSig] int ShutdownObject();
        [PreserveSig] int DetachObject();
    }

    [ComImport, Guid("bf94c121-5b05-4e6f-8000-ba598961414d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFTransform
    {
        [PreserveSig] int GetStreamLimits(out int pdwInputMinimum, out int pdwInputMaximum, out int pdwOutputMinimum, out int pdwOutputMaximum);
        [PreserveSig] int GetStreamCount(out int pcInputStreams, out int pcOutputStreams);
        [PreserveSig] int GetStreamIDs(int dwInputIDArraySize, [Out, MarshalAs(UnmanagedType.LPArray)] int[] pdwInputIDs, int dwOutputIDArraySize, [Out, MarshalAs(UnmanagedType.LPArray)] int[] pdwOutputIDs);
        [PreserveSig] int GetInputStreamInfo(int dwInputStreamID, out MFT_INPUT_STREAM_INFO pStreamInfo);
        [PreserveSig] int GetOutputStreamInfo(int dwOutputStreamID, out MFT_OUTPUT_STREAM_INFO pStreamInfo);
        [PreserveSig] int GetAttributes(out IMFAttributes pAttributes);
        [PreserveSig] int GetInputStreamAttributes(int dwInputStreamID, out IMFAttributes pAttributes);
        [PreserveSig] int GetOutputStreamAttributes(int dwOutputStreamID, out IMFAttributes pAttributes);
        [PreserveSig] int DeleteInputStream(int dwStreamID);
        [PreserveSig] int AddInputStreams(int cStreams, [In, MarshalAs(UnmanagedType.LPArray)] int[] adwStreamIDs);
        [PreserveSig] int GetInputAvailableType(int dwInputStreamID, int dwTypeIndex, out IMFMediaType ppType);
        [PreserveSig] int GetOutputAvailableType(int dwOutputStreamID, int dwTypeIndex, out IMFMediaType ppType);
        [PreserveSig] int SetInputType(int dwInputStreamID, IMFMediaType pType, int dwFlags);
        [PreserveSig] int SetOutputType(int dwOutputStreamID, IMFMediaType pType, int dwFlags);
        [PreserveSig] int GetInputCurrentType(int dwInputStreamID, out IMFMediaType ppType);
        [PreserveSig] int GetOutputCurrentType(int dwOutputStreamID, out IMFMediaType ppType);
        [PreserveSig] int GetInputStatus(int dwInputStreamID, out int pdwFlags);
        [PreserveSig] int GetOutputStatus(out int pdwFlags);
        [PreserveSig] int SetOutputBounds(long hnsLowerBound, long hnsUpperBound);
        [PreserveSig] int ProcessEvent(int dwInputStreamID, IntPtr pEvent);
        [PreserveSig] int ProcessMessage(int eMessage, IntPtr ulParam);
        [PreserveSig] int ProcessInput(int dwInputStreamID, IMFSample pSample, int dwFlags);
        [PreserveSig] int ProcessOutput(int dwFlags, int cOutputBufferCount, [In, Out, MarshalAs(UnmanagedType.LPArray)] MFT_OUTPUT_DATA_BUFFER[] pOutputSamples, out uint pdwStatus);
    }

    [ComImport, Guid("901db4c7-31ce-41a2-85dc-8fa0bf41b8da"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICodecAPI
    {
        [PreserveSig] int IsSupported(ref Guid Api);
        [PreserveSig] int IsModifiable(ref Guid Api);
        [PreserveSig] int GetParameterRange(ref Guid Api, IntPtr ValueMin, IntPtr ValueMax, IntPtr SteppingDelta);
        [PreserveSig] int GetParameterValues(ref Guid Api, IntPtr Values, out int ValuesCount);
        [PreserveSig] int GetDefaultValue(ref Guid Api, IntPtr Value);
        [PreserveSig] int GetValue(ref Guid Api, IntPtr Value);
        [PreserveSig] int SetValue(ref Guid Api, IntPtr Value);
        [PreserveSig] int RegisterForEvent(ref Guid Api, long userData);
        [PreserveSig] int UnregisterForEvent(ref Guid Api);
        [PreserveSig] int SetAllDefaults();
        [PreserveSig] int SetValueWithNotify(ref Guid Api, IntPtr Value, out IntPtr ChangedParam, out int ChangedParamCount);
        [PreserveSig] int SetAllDefaultsWithNotify(out IntPtr ChangedParam, out int ChangedParamCount);
        [PreserveSig] int GetAllSettings(IntPtr pStream);
        [PreserveSig] int SetAllSettings(IntPtr pStream);
        [PreserveSig] int SetAllSettingsWithNotify(IntPtr pStream, out IntPtr ChangedParam, out int ChangedParamCount);
    }

    internal static class MFNative
    {
        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFStartup(int Version, int dwFlags);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFShutdown();

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateMediaType(out IMFMediaType ppMFType);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateSample(out IMFSample ppIMFSample);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateMemoryBuffer(int cbMaxLength, out IMFMediaBuffer ppBuffer);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateAlignedMemoryBuffer(int cbMaxLength, int cbAligment, out IMFMediaBuffer ppBuffer);

        [DllImport("ole32.dll", ExactSpelling = true)]
        public static extern int CoCreateInstance(ref Guid clsid, IntPtr pUnkOuter, int dwClsContext, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

        [DllImport("ole32.dll", ExactSpelling = true)]
        public static extern int CoInitializeEx(IntPtr pvReserved, int dwCoInit);

        [DllImport("ole32.dll", ExactSpelling = true)]
        public static extern void CoUninitialize();

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFTEnumEx(
            Guid guidCategory,
            int Flags,
            IntPtr pInputType,
            IntPtr pOutputType,
            out IntPtr pppMFTActivate,
            out int pcMFTActivate);

        [DllImport("ole32.dll", ExactSpelling = true)]
        public static extern void CoTaskMemFree(IntPtr pv);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateDXGIDeviceManager(out int resetToken, out IMFDXGIDeviceManager ppDeviceManager);

        [DllImport("d3d11.dll", ExactSpelling = true)]
        public static extern int D3D11CreateDevice(
            IntPtr pAdapter,
            int DriverType,
            IntPtr Software,
            int Flags,
            IntPtr pFeatureLevels,
            int FeatureLevels,
            int SDKVersion,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppDevice,
            out int pFeatureLevel,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppImmediateContext);
    }

    internal static class MFHelpers
    {
        private static readonly object _logSync = new object();

        public static void Check(int hr, string what)
        {
            if (hr < 0)
            {
                throw new InvalidOperationException(what + " failed with HRESULT 0x" + hr.ToString("X8"));
            }
        }

        public static void Log(string message)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                var path = System.IO.Path.Combine(dir, "extentdesktop-error.log");
                var line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] [trace] " + message + "\r\n";
                lock (_logSync)
                {
                    System.IO.File.AppendAllText(path, line);
                }
            }
            catch
            {
            }
        }

        public static void LogHr(string what, int hr)
        {
            Log(what + " -> 0x" + hr.ToString("X8"));
        }

        public static ulong PackUInt64(uint high, uint low)
        {
            return ((ulong)high << 32) | low;
        }

        public static void SetConverterTypeFromAvailable(IMFTransform converter, bool isInput, Guid subtype, int width, int height, int fps, bool includeStride, string label)
        {
            for (int i = 0; i < 32; i++)
            {
                IMFMediaType template;
                int getHr = isInput
                    ? converter.GetInputAvailableType(0, i, out template)
                    : converter.GetOutputAvailableType(0, i, out template);
                if (getHr < 0)
                {
                    LogHr(label + " GetAvailableType[" + i + "]", getHr);
                    Check(getHr, label + " (no matching template)");
                    return;
                }

                try
                {
                    var subKey = MFGuids.MF_MT_SUBTYPE;
                    Guid templateSub;
                    if (template.GetGUID(ref subKey, out templateSub) != 0) continue;
                    if (templateSub != subtype) continue;

                    var sizeKey = MFGuids.MF_MT_FRAME_SIZE;
                    template.SetUINT64(ref sizeKey, PackUInt64((uint)width, (uint)height));

                    var rateKey = MFGuids.MF_MT_FRAME_RATE;
                    template.SetUINT64(ref rateKey, PackUInt64((uint)fps, 1));

                    var aspectKey = MFGuids.MF_MT_PIXEL_ASPECT_RATIO;
                    template.SetUINT64(ref aspectKey, PackUInt64(1, 1));

                    var interlaceKey = MFGuids.MF_MT_INTERLACE_MODE;
                    template.SetUINT32(ref interlaceKey, (uint)MFConstants.MFVideoInterlace_Progressive);

                    if (includeStride)
                    {
                        var strideKey = MFGuids.MF_MT_DEFAULT_STRIDE;
                        template.SetUINT32(ref strideKey, (uint)(width * 4));
                    }

                    int setHr = isInput
                        ? converter.SetInputType(0, template, 0)
                        : converter.SetOutputType(0, template, 0);
                    LogHr(label + " SetType(template[" + i + "])", setHr);
                    if (setHr >= 0) return;
                }
                finally
                {
                    Marshal.ReleaseComObject(template);
                }
            }

            Check(unchecked((int)0x80004005), label + " (no template accepted)");
        }

        public static IMFMediaType CreateVideoType(Guid subtype, int width, int height, int fpsNum, int fpsDen)
        {
            IMFMediaType type;
            Check(MFNative.MFCreateMediaType(out type), "MFCreateMediaType");

            var majorTypeKey = MFGuids.MF_MT_MAJOR_TYPE;
            var majorTypeVal = MFGuids.MFMediaType_Video;
            Check(type.SetGUID(ref majorTypeKey, ref majorTypeVal), "SetGUID(MAJOR_TYPE)");

            var subtypeKey = MFGuids.MF_MT_SUBTYPE;
            Check(type.SetGUID(ref subtypeKey, ref subtype), "SetGUID(SUBTYPE)");

            var sizeKey = MFGuids.MF_MT_FRAME_SIZE;
            Check(type.SetUINT64(ref sizeKey, PackUInt64((uint)width, (uint)height)), "SetUINT64(FRAME_SIZE)");

            var rateKey = MFGuids.MF_MT_FRAME_RATE;
            Check(type.SetUINT64(ref rateKey, PackUInt64((uint)fpsNum, (uint)fpsDen)), "SetUINT64(FRAME_RATE)");

            var aspectKey = MFGuids.MF_MT_PIXEL_ASPECT_RATIO;
            Check(type.SetUINT64(ref aspectKey, PackUInt64(1, 1)), "SetUINT64(PIXEL_ASPECT_RATIO)");

            var interlaceKey = MFGuids.MF_MT_INTERLACE_MODE;
            Check(type.SetUINT32(ref interlaceKey, (uint)MFConstants.MFVideoInterlace_Progressive), "SetUINT32(INTERLACE_MODE)");

            return type;
        }
    }
}
