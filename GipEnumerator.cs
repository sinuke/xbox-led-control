using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace XboxLedControl;

/// <summary>
/// Enumerates all USB-connected Xbox controllers via \\.\XboxGIP.
/// After triggering re-enumeration, reads announcement frames and collects
/// unique device IDs (6-byte MACs) until the frame queue is drained.
/// </summary>
internal static class GipEnumerator
{
    private const uint IOCTL_GIP_REENUMERATE = 0x40001CD0;
    private const int  ERROR_NO_MORE_ITEMS   = 259;

    /// <summary>
    /// Opens \\.\XboxGIP, triggers re-enumeration, and returns all device IDs
    /// collected from controller announcement frames.
    /// Returns an empty list if the driver cannot be opened or no controllers respond.
    /// </summary>
    internal static IReadOnlyList<byte[]> ReadAllDeviceIds(bool verbose = false, int timeoutMs = 1000)
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

    private static IReadOnlyList<byte[]> ReadAll(SafeFileHandle hFile, int timeoutMs, bool verbose)
    {
        var seen  = new Dictionary<string, byte[]>();
        var buf   = new byte[1024];
        var sw    = Stopwatch.StartNew();
        int idleMs = 0;

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            bool ok = NativeMethods.ReadFile(hFile, buf, (uint)buf.Length, out uint bytesRead, IntPtr.Zero);

            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                if (err != ERROR_NO_MORE_ITEMS)
                    break;

                idleMs += 20;
                // Stop early once the queue has been empty for 200 ms and we already found
                // at least one controller — avoids waiting out the full timeout needlessly.
                if (seen.Count > 0 && idleMs >= 200)
                    break;

                Thread.Sleep(20);
                continue;
            }

            idleMs = 0;

            if (bytesRead >= 6)
            {
                var mac = buf[0..6];
                string key = BitConverter.ToString(mac);
                if (seen.TryAdd(key, mac) && verbose)
                    Console.WriteLine($"  Found controller: {key.Replace('-', ':')}");
            }
        }

        return seen.Values.ToList();
    }
}
