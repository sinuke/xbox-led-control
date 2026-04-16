using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

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
    private const int  ERROR_NO_MORE_ITEMS   = 259;

    /// <summary>
    /// Send LED command via \\.\XboxGIP. Returns true if WriteFile succeeded.
    /// rawGip must be the 7-byte GIP frame from GipLedCommand.BuildRaw().
    /// </summary>
    public static bool TrySend(byte[] rawGip, bool verbose)
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
            return false;
        }
        if (verbose) Console.WriteLine("  Handle opened OK.");

        bool ioctlOk = NativeMethods.DeviceIoControl(
            hFile, IOCTL_GIP_REENUMERATE,
            IntPtr.Zero, 0, IntPtr.Zero, 0,
            out _, IntPtr.Zero);
        if (verbose) Console.WriteLine($"  Re-enumerate IOCTL: {(ioctlOk ? "OK" : $"failed ({Marshal.GetLastWin32Error()}), continuing")}");

        byte[]? mac = ReadMac(hFile, timeoutMs: 1000, verbose);
        if (mac == null)
        {
            if (verbose) Console.WriteLine("  No controller announced (timeout).");
            return false;
        }
        if (verbose) Console.WriteLine($"  Device ID: {BitConverter.ToString(mac).Replace('-', ':')}");

        byte[] buf = BuildFrame(mac, rawGip);
        if (verbose) Console.WriteLine($"  Write frame ({buf.Length}B = 20 hdr + {rawGip[3]} payload): {BitConverter.ToString(buf)}");

        bool ok = NativeMethods.WriteFile(hFile, buf, (uint)buf.Length, out uint written, IntPtr.Zero);
        if (verbose) Console.WriteLine(ok ? $"  WriteFile OK ({written}B written)" : $"  WriteFile FAILED (error {Marshal.GetLastWin32Error()})");

        return ok;
    }

    internal static byte[] BuildFrame(byte[] mac, byte[] rawGip)
    {
        byte payloadLen = rawGip[3];
        // Frame size must be exactly 20 + payloadLen.
        // The driver validates frameSize - 20 == PayloadLen field; extra bytes → INVALID_PARAMETER.
        var buf = new byte[20 + payloadLen];
        mac.AsSpan(0, 6).CopyTo(buf);               // Device ID
        rawGip.AsSpan(0, 3).CopyTo(buf.AsSpan(8)); // MessageType, Flags, SequenceId
        buf[12] = payloadLen;                       // PayloadLen LE byte 0
        // [13-19] = 0x00
        rawGip.AsSpan(4, payloadLen).CopyTo(buf.AsSpan(20)); // payload
        return buf;
    }

    private static byte[]? ReadMac(SafeFileHandle hFile, int timeoutMs, bool verbose = false)
    {
        if (verbose) Console.WriteLine("  Reading controller device ID...");
        var sw = Stopwatch.StartNew();
        var buf = new byte[1024];

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            bool ok = NativeMethods.ReadFile(hFile, buf, (uint)buf.Length, out uint bytesRead, IntPtr.Zero);

            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == ERROR_NO_MORE_ITEMS)
                {
                    Thread.Sleep(20);
                    continue;
                }
                if (verbose) Console.WriteLine($"  ReadFile error {err}");
                return null;
            }

            if (bytesRead >= 6)
                return buf[0..6];

            Thread.Sleep(10);
        }

        return null;
    }
}
