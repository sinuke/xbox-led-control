using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static XboxLedControl.NativeMethods;

namespace XboxLedControl;

public enum ConnectionType { Unknown, Usb, Bluetooth }

public record XboxDevice(
    string         Path,
    ushort         VendorId,
    ushort         ProductId,
    string         ProductName,
    ConnectionType Connection);

/// <summary>
/// Enumerates ALL HID interfaces belonging to Microsoft (VID 0x045E) devices.
///
/// Connection detection heuristic:
///   Bluetooth path contains the BT HID Service UUID:
///     {00001812-0000-1000-8000-00805f9b34fb}
///   USB (xusb22.sys) path starts right after "hid#" with "vid_":
///     hid#vid_045e&pid_xxxx&ig_00#...
/// </summary>
public static class XboxControllerFinder
{
    private const ushort XBOX_VID = 0x045E;

    // Bluetooth HID (Human Interface Device) Service class UUID
    private const string BT_HID_UUID = "00001812-0000-1000-8000-00805f9b34fb";

    public static List<XboxDevice> Find()
    {
        var result = new List<XboxDevice>();
        HidD_GetHidGuid(out Guid hidGuid);

        IntPtr devInfoSet = SetupDiGetClassDevs(
            ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

        if (devInfoSet == new IntPtr(-1))
        {
            Console.Error.WriteLine($"SetupDiGetClassDevs failed: {Marshal.GetLastWin32Error()}");
            return result;
        }

        try
        {
            var ifData = new SP_DEVICE_INTERFACE_DATA
            {
                cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>()
            };

            for (uint idx = 0;
                 SetupDiEnumDeviceInterfaces(devInfoSet, IntPtr.Zero, ref hidGuid, idx, ref ifData);
                 idx++)
            {
                string? path = GetDevicePath(devInfoSet, ref ifData);
                if (path is null) continue;
                if (!path.Contains("vid_045e", StringComparison.OrdinalIgnoreCase)) continue;

                XboxDevice? dev = TryInspect(path);
                if (dev is not null) result.Add(dev);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(devInfoSet);
        }

        return result;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string? GetDevicePath(IntPtr devInfoSet, ref SP_DEVICE_INTERFACE_DATA ifData)
    {
        SetupDiGetDeviceInterfaceDetailW(devInfoSet, ref ifData,
            IntPtr.Zero, 0, out uint needed, IntPtr.Zero);
        if (needed == 0) return null;

        IntPtr buf = Marshal.AllocHGlobal((int)needed);
        try
        {
            Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 5);
            if (!SetupDiGetDeviceInterfaceDetailW(devInfoSet, ref ifData, buf, needed, out _, IntPtr.Zero))
                return null;
            return Marshal.PtrToStringUni(buf + 4);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static XboxDevice? TryInspect(string path)
    {
        SafeFileHandle h = CreateFile(path, 0,
            FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
            OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);

        if (h.IsInvalid) return null;

        using (h)
        {
            var attrs = new HIDD_ATTRIBUTES { Size = (uint)Marshal.SizeOf<HIDD_ATTRIBUTES>() };
            if (!HidD_GetAttributes(h, ref attrs)) return null;
            if (attrs.VendorID != XBOX_VID) return null;

            string name = ReadProductString(h) ?? $"Xbox Device (PID 0x{attrs.ProductID:X4})";
            ConnectionType conn = DetectConnection(path);

            return new XboxDevice(path, attrs.VendorID, attrs.ProductID, name, conn);
        }
    }

    /// <summary>
    /// Read product string via unmanaged buffer to avoid StringBuilder
    /// marshaling issues with Bluetooth HID devices.
    /// </summary>
    private static string? ReadProductString(SafeFileHandle h)
    {
        const int bufBytes = 512; // 256 UTF-16 characters
        IntPtr buf = Marshal.AllocHGlobal(bufBytes);
        try
        {
            // Zero the buffer so PtrToStringUni stops at a genuine null terminator
            for (int i = 0; i < bufBytes; i += 4)
                Marshal.WriteInt32(buf + i, 0);

            if (!HidD_GetProductString(h, buf, (uint)bufBytes))
                return null;

            string s = Marshal.PtrToStringUni(buf) ?? "";
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    /// <summary>
    /// Detects connection type from the Windows device-instance path.
    ///
    /// Bluetooth HID devices on Windows always contain the BT HID service UUID
    /// in their path, regardless of whether they also have ig_XX suffix.
    /// Example BT path:
    ///   \hid\{00001812-0000-1000-8000-00805f9b34fb}&dev&vid_045e&pid_0b13&…&ig_00#…
    ///
    /// USB (xusb22.sys) path has no GUID prefix:
    ///   \hid\vid_045e&pid_02ff&ig_00#…
    /// </summary>
    private static ConnectionType DetectConnection(string path)
    {
        if (path.Contains(BT_HID_UUID, StringComparison.OrdinalIgnoreCase))
            return ConnectionType.Bluetooth;

        // Fallback: older BT stack may use "bth" prefix
        if (path.Contains("bthidbus", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("bthhfenum", StringComparison.OrdinalIgnoreCase))
            return ConnectionType.Bluetooth;

        // USB: path segment after "hid#" starts directly with "vid_"
        int hidIdx = path.IndexOf("hid#", StringComparison.OrdinalIgnoreCase);
        if (hidIdx >= 0 && path.Length > hidIdx + 4)
        {
            string afterHid = path[(hidIdx + 4)..];
            if (afterHid.StartsWith("vid_", StringComparison.OrdinalIgnoreCase))
                return ConnectionType.Usb;
        }

        return ConnectionType.Unknown;
    }
}
