using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace XboxLedControl;

/// <summary>
/// P/Invoke declarations for Windows HID and SetupAPI.
/// </summary>
internal static class NativeMethods
{
    // ── HID ────────────────────────────────────────────────────────────────

    [DllImport("hid.dll", SetLastError = true)]
    internal static extern void HidD_GetHidGuid(out Guid HidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_GetAttributes(
        SafeFileHandle HidDeviceObject,
        ref HIDD_ATTRIBUTES Attributes);

    // Use IntPtr overload to avoid StringBuilder marshaling issues with BT HID devices
    [DllImport("hid.dll", EntryPoint = "HidD_GetProductString", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_GetProductString(
        SafeFileHandle HidDeviceObject,
        IntPtr Buffer,
        uint BufferLength);

    /// <summary>
    /// Sends a complete output report to the device.
    /// Does not require Write access on some drivers — useful for shared devices.
    /// </summary>
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_SetOutputReport(
        SafeFileHandle HidDeviceObject,
        [In] byte[] ReportBuffer,
        uint ReportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_SetFeature(
        SafeFileHandle HidDeviceObject,
        [In] byte[] ReportBuffer,
        uint ReportBufferLength);

    // ── SetupAPI ───────────────────────────────────────────────────────────

    [DllImport("setupapi.dll", SetLastError = true)]
    internal static extern IntPtr SetupDiGetClassDevs(
        ref Guid ClassGuid,
        IntPtr Enumerator,
        IntPtr hwndParent,
        uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr DeviceInfoSet,
        IntPtr DeviceInfoData,
        ref Guid InterfaceClassGuid,
        uint MemberIndex,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    // First overload: pass IntPtr.Zero to query required size
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceInterfaceDetailW(
        IntPtr DeviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
        IntPtr DeviceInterfaceDetailData,
        uint DeviceInterfaceDetailDataSize,
        out uint RequiredSize,
        IntPtr DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    // ── Kernel32 ───────────────────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WriteFile(
        SafeFileHandle hFile,
        [In] byte[] lpBuffer,
        uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadFile(
        SafeFileHandle hFile,
        [Out] byte[] lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_GetPreparsedData(
        SafeFileHandle HidDeviceObject,
        out IntPtr PreparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);

    // Returns HIDP_STATUS — 0x00110000 = HIDP_STATUS_SUCCESS
    [DllImport("hid.dll")]
    internal static extern int HidP_GetCaps(IntPtr PreparsedData, out HIDP_CAPS Capabilities);

    // ── Constants ──────────────────────────────────────────────────────────

    internal const uint DIGCF_PRESENT         = 0x00000002;
    internal const uint DIGCF_DEVICEINTERFACE = 0x00000010;
    internal const uint GENERIC_READ          = 0x80000000;
    internal const uint GENERIC_WRITE         = 0x40000000;
    internal const uint FILE_SHARE_READ       = 0x00000001;
    internal const uint FILE_SHARE_WRITE      = 0x00000002;
    internal const uint OPEN_EXISTING         = 3;
    internal const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    // ── Structs ────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVICE_INTERFACE_DATA
    {
        public uint  cbSize;
        public Guid  InterfaceClassGuid;
        public uint  Flags;
        private UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HIDD_ATTRIBUTES
    {
        public uint   Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;   // ← total output report size incl. report-ID byte
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }
}
