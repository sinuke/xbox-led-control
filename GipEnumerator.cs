using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace XboxLedControl;

/// <summary>
/// Enumerates all USB-connected Xbox controllers via \\.\XboxGIP.
/// After triggering re-enumeration, reads announcement and metadata frames
/// to collect device IDs and product information.
/// </summary>
internal static class GipEnumerator
{
    private const uint IOCTL_GIP_REENUMERATE = 0x40001CD0;
    private const int  ERROR_NO_MORE_ITEMS   = 259;

    /// <summary>
    /// Opens \\.\XboxGIP, triggers re-enumeration, and returns info for all
    /// controllers collected from announcement and metadata frames.
    /// Returns an empty list if the driver cannot be opened or no controllers respond.
    /// </summary>
    internal static IReadOnlyList<ControllerInfo> ReadAllControllers(bool verbose = false, int timeoutMs = 1000)
    {
        if (verbose) Console.WriteLine("Opening \\\\.\\XboxGIP...");

        using var hFile = NativeMethods.CreateFile(
            @"\\.\XboxGIP",
            NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_ATTRIBUTE_NORMAL,
            IntPtr.Zero);

        if (hFile.IsInvalid)
        {
            if (verbose) Console.WriteLine($"  Failed to open: Win32 error {Marshal.GetLastWin32Error()}");
            return [];
        }
        if (verbose) Console.WriteLine("  Handle opened OK.");

        bool ioctlOk = NativeMethods.DeviceIoControl(
            hFile, IOCTL_GIP_REENUMERATE,
            IntPtr.Zero, 0, IntPtr.Zero, 0,
            out _, IntPtr.Zero);
        if (verbose) Console.WriteLine($"  Re-enumerate IOCTL: {(ioctlOk ? "OK" : $"failed ({Marshal.GetLastWin32Error()}), continuing")}");

        return ReadAll(hFile, timeoutMs, verbose);
    }

    private static IReadOnlyList<ControllerInfo> ReadAll(SafeFileHandle hFile, int timeoutMs, bool verbose)
    {
        var devices = new Dictionary<string, ControllerInfo>();
        var buf     = new byte[4096];
        var sw      = Stopwatch.StartNew();
        int idleMs  = 0;

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            bool ok = NativeMethods.ReadFile(hFile, buf, (uint)buf.Length, out uint bytesRead, IntPtr.Zero);

            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                if (err != ERROR_NO_MORE_ITEMS)
                    break;

                idleMs += 20;
                if (devices.Count > 0 && idleMs >= 200)
                    break;

                Thread.Sleep(20);
                continue;
            }

            idleMs = 0;

            if (bytesRead < 6) continue;

            string key = BitConverter.ToString(buf, 0, 6);

            // Track the device MAC from any frame.
            if (!devices.ContainsKey(key))
            {
                devices[key] = new ControllerInfo(buf[0..6], 0, 0);
                if (verbose) Console.WriteLine($"  Found controller: {key.Replace('-', ':')}");
            }

            // Opportunistically extract VendorId / ProductId from the first metadata
            // fragment (bytes 0-3 of the blob = VendorId U16 LE, ProductId U16 LE).
            MetadataFragment? frag = GipMetadataCommand.ParseTransportFrame(buf, (int)bytesRead);
            if (frag?.Type is FragmentType.Complete or FragmentType.Initial &&
                frag.Value.Data.Length >= 4)
            {
                ushort vid = BitConverter.ToUInt16(frag.Value.Data, 0);
                ushort pid = BitConverter.ToUInt16(frag.Value.Data, 2);
                devices[key] = devices[key] with { VendorId = vid, ProductId = pid };
                if (verbose) Console.WriteLine($"  Metadata for {key.Replace('-', ':')}: VID=0x{vid:X4} PID=0x{pid:X4}");
            }
        }

        return devices.Values.ToList();
    }
}

/// <summary>Identifies a connected controller with its USB product information.</summary>
internal record ControllerInfo(byte[] Mac, ushort VendorId, ushort ProductId);
