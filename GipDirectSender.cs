using System.Runtime.InteropServices;

namespace XboxLedControl;

/// <summary>
/// Sends a GIP LED command directly to \\.\XboxGIP (USB + xboxgip.sys, no admin needed).
///
/// Working format (confirmed): read-symmetric 23-byte frame with seq=0x00:
///   [0-5]   Controller device ID (from ReadFile frames)
///   [6-7]   00 00
///   [8]     MessageType = 0x0A  (GIP Command class, message #10)
///   [9]     Flags = 0x20
///   [10-11] 00 00  (seq=0x00 bypasses driver deduplication check)
///   [12-15] PayloadLen 4-byte LE (= 0x03)
///   [16-19] 00 00 00 00
///   [20]    Sub-command = 0x00
///   [21]    Pattern
///   [22]    Intensity (0–47)
/// </summary>
internal static class GipDirectSender
{
    private const uint IOCTL_GIP_REENUMERATE = 0x40001CD0;

    /// <summary>
    /// Send LED command via \\.\XboxGIP. Returns true if WriteFile succeeded.
    /// rawGip must be the 7-byte GIP frame from GipLedCommand.BuildRaw().
    /// </summary>
    public static bool TrySend(byte[] rawGip, bool debug = false)
    {
        if (debug) Console.WriteLine("Opening \\\\.\\XboxGIP...");

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
            if (debug) Console.WriteLine($"  Failed to open: Win32 error {Marshal.GetLastWin32Error()}");
            return false;
        }
        if (debug) Console.WriteLine("  Handle opened OK.");

        bool ioctlOk = NativeMethods.DeviceIoControl(
            hFile, IOCTL_GIP_REENUMERATE,
            IntPtr.Zero, 0, IntPtr.Zero, 0,
            out _, IntPtr.Zero);
        if (debug) Console.WriteLine($"  Re-enumerate IOCTL: {(ioctlOk ? "OK" : $"failed ({Marshal.GetLastWin32Error()}), continuing")}");

        byte[]? mac = ReadMac(hFile, timeoutMs: 1000, debug);
        if (mac == null)
        {
            if (debug) Console.WriteLine("  No controller announced (timeout).");
            return false;
        }
        if (debug) Console.WriteLine($"  Device ID: {string.Join(":", mac.Select(b => $"{b:X2}"))}");

        byte[] buf = BuildFrame(mac, rawGip);
        if (debug) Console.WriteLine($"  Write frame ({buf.Length}B = 20 hdr + {rawGip[3]} payload): {BitConverter.ToString(buf)}");

        bool ok = NativeMethods.WriteFile(hFile, buf, (uint)buf.Length, out uint written, IntPtr.Zero);
        if (debug) Console.WriteLine(ok ? $"  WriteFile OK ({written}B written)" : $"  WriteFile FAILED (error {Marshal.GetLastWin32Error()})");

        return ok;
    }

    private static byte[] BuildFrame(byte[] mac, byte[] rawGip)
    {
        byte payloadLen = rawGip[3];
        // Frame size must be exactly 20 + payloadLen.
        // The driver validates frameSize - 20 == PayloadLen field; extra bytes → INVALID_PARAMETER.
        var buf = new byte[20 + payloadLen];
        Array.Copy(mac, buf, 6);
        buf[8]  = rawGip[0];                        // MessageType
        buf[9]  = rawGip[1];                        // Flags
        buf[10] = rawGip[2];                        // SequenceId (0x00 for LED bypasses dedup; non-zero for other commands)
        buf[12] = payloadLen;                       // PayloadLen LE byte 0
        // [13-19] = 0x00
        Array.Copy(rawGip, 4, buf, 20, payloadLen); // payload (trailing bytes stay 0x00)
        return buf;
    }

    private static byte[]? ReadMac(
        Microsoft.Win32.SafeHandles.SafeFileHandle hFile, int timeoutMs, bool debug = false)
    {
        if (debug) Console.WriteLine("  Reading controller device ID...");
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var buf = new byte[1024];

        while (DateTime.UtcNow < deadline)
        {
            bool ok = NativeMethods.ReadFile(hFile, buf, (uint)buf.Length, out uint bytesRead, IntPtr.Zero);

            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == 259)  // ERROR_NO_MORE_ITEMS
                {
                    System.Threading.Thread.Sleep(20);
                    continue;
                }
                if (debug) Console.WriteLine($"  ReadFile error {err}");
                return null;
            }

            if (bytesRead >= 6)
                return buf[0..6];

            System.Threading.Thread.Sleep(10);
        }

        return null;
    }
}
