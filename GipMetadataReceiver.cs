using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace XboxLedControl;

/// <summary>
/// Reads the GIP Metadata from \\.\XboxGIP by listening to frames the controller
/// sends automatically after re-enumeration (MS-GIPUSB §3.1.5.5.4).
/// No explicit request is sent — the driver does not forward host-initiated writes
/// for any command other than LED.
/// </summary>
internal static class GipMetadataReceiver
{
    private const uint IOCTL_GIP_REENUMERATE = 0x40001CD0;
    private const int  ERROR_NO_MORE_ITEMS   = 259;
    private const int  RESPONSE_TIMEOUT_MS   = 5000;

    /// <summary>
    /// Open \\.\XboxGIP, trigger re-enumeration, and collect the metadata blob
    /// the controller broadcasts during its enumeration sequence.
    /// When <paramref name="deviceId"/> is provided, only frames from that device
    /// are considered; otherwise the first responding device is used.
    /// Returns <see langword="null"/> on failure or timeout.
    /// </summary>
    public static byte[]? TryReceive(bool verbose, byte[]? deviceId = null)
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
            Console.Error.WriteLine($"Failed to open \\\\.\\XboxGIP: Win32 error {Marshal.GetLastWin32Error()}.");
            return null;
        }
        if (verbose) Console.WriteLine("  Handle opened OK.");

        bool ioctlOk = NativeMethods.DeviceIoControl(
            hFile, IOCTL_GIP_REENUMERATE,
            IntPtr.Zero, 0, IntPtr.Zero, 0,
            out _, IntPtr.Zero);
        if (verbose) Console.WriteLine($"  Re-enumerate IOCTL: {(ioctlOk ? "OK" : $"failed ({Marshal.GetLastWin32Error()}), continuing")}");

        if (deviceId is not null && verbose)
            Console.WriteLine($"  Filtering for device: {BitConverter.ToString(deviceId).Replace('-', ':')}");

        return ReadAndReassemble(hFile, deviceId, verbose);
    }

    private static byte[]? ReadAndReassemble(SafeFileHandle hFile, byte[]? filterDeviceId, bool verbose)
    {
        byte[]? buffer = null;
        uint totalLength = 0;
        var sw = Stopwatch.StartNew();
        var readBuf = new byte[4096];

        while (sw.ElapsedMilliseconds < RESPONSE_TIMEOUT_MS)
        {
            bool ok = NativeMethods.ReadFile(hFile, readBuf, (uint)readBuf.Length, out uint bytesRead, IntPtr.Zero);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == ERROR_NO_MORE_ITEMS) { Thread.Sleep(20); continue; }
                if (verbose) Console.WriteLine($"  ReadFile error {err}.");
                return null;
            }

            if (bytesRead < 6) continue;

            if (filterDeviceId is not null && !DeviceMatches(readBuf, filterDeviceId))
                continue;

            MetadataFragment? frag = GipMetadataCommand.ParseTransportFrame(readBuf, (int)bytesRead);
            if (frag is null) continue;

            if (verbose && buffer is null && frag.Value.Type != FragmentType.MetadataComplete)
            {
                string mac = BitConverter.ToString(readBuf, 0, 6).Replace('-', ':');
                Console.WriteLine($"  Metadata from device: {mac}");
            }

            switch (frag.Value.Type)
            {
                case FragmentType.Complete:
                    if (verbose) Console.WriteLine($"  Non-fragmented response: {frag.Value.Data.Length} bytes");
                    return frag.Value.Data;

                case FragmentType.Initial:
                    totalLength = frag.Value.TotalLength;
                    buffer = new byte[totalLength];
                    frag.Value.Data.CopyTo(buffer, 0);
                    if (verbose) Console.WriteLine($"  Initial fragment: {frag.Value.Data.Length}/{totalLength} bytes");
                    break;

                case FragmentType.Middle:
                    if (buffer is null) break;
                    frag.Value.Data.CopyTo(buffer, (int)frag.Value.Offset);
                    if (verbose) Console.WriteLine($"  Middle fragment at +{frag.Value.Offset}: {frag.Value.Data.Length} bytes");
                    break;

                case FragmentType.Final:
                    if (buffer is null) break;
                    frag.Value.Data.CopyTo(buffer, (int)frag.Value.Offset);
                    if (verbose) Console.WriteLine($"  Final fragment at +{frag.Value.Offset}: {frag.Value.Data.Length} bytes → {totalLength} total");
                    return buffer;

                case FragmentType.MetadataComplete:
                    if (verbose) Console.WriteLine("  MetadataComplete signal received.");
                    return buffer;
            }
        }

        Console.Error.WriteLine("Failed: no metadata received (timeout).");
        return null;
    }

    private static bool DeviceMatches(byte[] frame, byte[] deviceId)
    {
        for (int i = 0; i < 6; i++)
            if (frame[i] != deviceId[i]) return false;
        return true;
    }
}
