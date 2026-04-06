using System.ComponentModel;
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
///
/// Previously tried formats that failed (INVALID_PARAMETER):
///   Any MessageType ≠ 0x0A — driver validates GIP message type
///   MAC + raw GIP with seq > 0x00 on second call — driver rejects duplicate seq
/// </summary>
internal static class GipDirectSender
{
    private const uint IOCTL_GIP_REENUMERATE = 0x40001CD0;

    /// <summary>
    /// Send LED command via \\.\XboxGIP. Returns true if WriteFile succeeded.
    /// rawGip must be the 7-byte GIP frame from GipLedCommand.BuildRaw().
    /// </summary>
    public static bool TrySend(byte[] rawGip)
    {
        using var hFile = NativeMethods.CreateFile(
            @"\\.\XboxGIP",
            NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_ATTRIBUTE_NORMAL,
            IntPtr.Zero);

        if (hFile.IsInvalid) return false;

        NativeMethods.DeviceIoControl(
            hFile, IOCTL_GIP_REENUMERATE,
            IntPtr.Zero, 0, IntPtr.Zero, 0,
            out _, IntPtr.Zero);

        byte[]? mac = ReadMac(hFile, timeoutMs: 1000);
        if (mac == null) return false;

        byte[] buf = BuildFrame(mac, rawGip);
        return NativeMethods.WriteFile(hFile, buf, (uint)buf.Length, out _, IntPtr.Zero);
    }

    /// <summary>
    /// Mirrors the root TrySend flow (open → IOCTL → ReadMac → WriteFile → close)
    /// but sends only the 25-byte CRC-16-CCITT variant instead of the working 23B frame.
    /// Tries both big-endian and little-endian CRC on the same handle.
    /// Used by --gip-crc.
    /// </summary>
    public static void TrySendCrc(GipLedPattern pattern, byte intensity)
    {
        Console.WriteLine($"Opening \\\\.\\XboxGIP...");

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
            int err = Marshal.GetLastWin32Error();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  Failed to open: error {err} (0x{err:X8})");
            Console.ResetColor();
            return;
        }
        Console.WriteLine("  Handle opened OK.");

        bool ioctlOk = NativeMethods.DeviceIoControl(
            hFile, IOCTL_GIP_REENUMERATE,
            IntPtr.Zero, 0, IntPtr.Zero, 0,
            out _, IntPtr.Zero);
        Console.WriteLine($"  Re-enumerate IOCTL: {(ioctlOk ? "OK" : $"failed ({Marshal.GetLastWin32Error()}), continuing")}");

        byte[]? mac = ReadMac(hFile, timeoutMs: 1000);
        if (mac == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  No controller announced (timeout).");
            Console.ResetColor();
            return;
        }
        Console.WriteLine($"  Device ID: {string.Join(":", mac.Select(b => $"{b:X2}"))}");

        byte[] gip = GipLedCommand.BuildRaw(pattern, intensity);
        Console.WriteLine($"  rawGip (7B): {BitConverter.ToString(gip)}");

        ushort crc = Crc16Ccitt(gip, 0, gip.Length);
        Console.WriteLine($"  CRC-16-CCITT: 0x{crc:X4}  (BE: {crc >> 8:X2} {crc & 0xFF:X2}  LE: {crc & 0xFF:X2} {crc >> 8:X2})");
        Console.WriteLine();

        TryWrite(hFile, "read-symmetric 25B + CRC16-CCITT BE (payloadLen=5)", BuildFrameWithCrc(mac, gip, bigEndian: true));
        TryWrite(hFile, "read-symmetric 25B + CRC16-CCITT LE (payloadLen=5)", BuildFrameWithCrc(mac, gip, bigEndian: false));
    }

    /// <summary>
    /// Diagnostic probe — tries multiple write formats and an IOCTL sweep.
    /// Used by --gip-send.
    /// </summary>
    public static void ProbeFormats(GipLedPattern pattern, byte intensity)
    {
        Console.WriteLine($"Opening \\\\.\\XboxGIP...");

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
            int err = Marshal.GetLastWin32Error();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  Failed to open: error {err} (0x{err:X8})");
            Console.ResetColor();
            return;
        }

        NativeMethods.DeviceIoControl(
            hFile, IOCTL_GIP_REENUMERATE,
            IntPtr.Zero, 0, IntPtr.Zero, 0,
            out _, IntPtr.Zero);

        byte[]? mac = ReadMac(hFile, timeoutMs: 1000);
        if (mac == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  No GIP controller announced — no frames received.");
            Console.ResetColor();
            return;
        }

        string macStr = string.Join(":", mac.Select(b => b.ToString("X2")));
        Console.WriteLine($"  Controller device ID: {macStr}");
        Console.WriteLine($"  Sending LED: pattern={pattern} intensity={intensity}/47");
        Console.WriteLine();

        byte[] gip = GipLedCommand.BuildRaw(pattern, intensity);

        Console.WriteLine("  --- WriteFile variants ---");
        TryWrite(hFile, "no-MAC, GIP padded 20B",           Pad(gip, 20));
        TryWrite(hFile, "MAC + GIP padded 20B",             Pad(PrefixMac(mac, gip), 20));
        TryWrite(hFile, "MAC + GIP padded 64B",             Pad(PrefixMac(mac, gip), 64));
        TryWrite(hFile, "read-symmetric 23B  ← working",   BuildFrame(mac, gip));
        TryWrite(hFile, "read-symmetric padded 64B",        Pad(BuildFrame(mac, gip), 64));

        Console.WriteLine();
        Console.WriteLine("  --- CRC-16-CCITT variants (poly=0x1021, init=0xFFFF, computed over rawGip[0..6]) ---");
        ushort crc = Crc16Ccitt(gip, 0, gip.Length);
        Console.WriteLine($"  CRC value: 0x{crc:X4}  (BE: {crc >> 8:X2} {crc & 0xFF:X2}  LE: {crc & 0xFF:X2} {crc >> 8:X2})");
        TryWrite(hFile, "read-symmetric 25B + CRC16-CCITT BE (payloadLen=5)", BuildFrameWithCrc(mac, gip, bigEndian: true));
        TryWrite(hFile, "read-symmetric 25B + CRC16-CCITT LE (payloadLen=5)", BuildFrameWithCrc(mac, gip, bigEndian: false));

        Console.WriteLine();
        Console.WriteLine("  --- IOCTL probe (0x40001C80–0x40001D00, needs Admin for most) ---");
        byte[] ioctlInput = Pad(PrefixMac(mac, gip), 64);
        for (uint code = 0x40001C80; code <= 0x40001D00; code += 4)
        {
            if (code == IOCTL_GIP_REENUMERATE) continue;
            TryIoctlOut(hFile, code, ioctlInput, 256);
        }
    }

    // ── Write / IOCTL helpers ─────────────────────────────────────────────────

    private static void TryWrite(
        Microsoft.Win32.SafeHandles.SafeFileHandle hFile,
        string label, byte[] cmd)
    {
        int showLen = Math.Min(cmd.Length, 23);
        string hex  = BitConverter.ToString(cmd, 0, showLen) + (cmd.Length > showLen ? "…" : "");
        Console.Write($"  WriteFile [{label}]  ({cmd.Length}B) [{hex}]  ");

        bool ok = NativeMethods.WriteFile(hFile, cmd, (uint)cmd.Length, out uint written, IntPtr.Zero);

        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            string note = err switch
            {
                1167 => "DEVICE_NOT_CONNECTED",
                87   => "INVALID_PARAMETER",
                122  => "BUFFER_TOO_SMALL",
                5    => "ACCESS_DENIED",
                _    => $"err={err}"
            };
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"FAIL  {note} (0x{err:X})");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"OK  ({written}B written)");
            Console.ResetColor();
        }
    }

    private static void TryIoctlOut(
        Microsoft.Win32.SafeHandles.SafeFileHandle hFile,
        uint code, byte[] inBuf, int outSize)
    {
        Console.Write($"  IOCTL 0x{code:X8} in={inBuf.Length}B out={outSize}B  ");

        var outBuf = outSize > 0 ? new byte[outSize] : Array.Empty<byte>();

        unsafe
        {
            fixed (byte* pIn  = inBuf.Length  > 0 ? inBuf  : new byte[1])
            fixed (byte* pOut = outBuf.Length > 0 ? outBuf : new byte[1])
            {
                bool ok = NativeMethods.DeviceIoControl(
                    hFile, code,
                    inBuf.Length  > 0 ? (IntPtr)pIn  : IntPtr.Zero, (uint)inBuf.Length,
                    outBuf.Length > 0 ? (IntPtr)pOut : IntPtr.Zero, (uint)outBuf.Length,
                    out uint ret, IntPtr.Zero);

                int lastErr = ok ? 0 : Marshal.GetLastWin32Error();
                bool moreData = lastErr == 234;

                if (!ok && !moreData)
                {
                    string note = lastErr switch
                    {
                        1   => "NOT_SUPPORTED",
                        2   => "NOT_FOUND",
                        5   => "ACCESS_DENIED",
                        87  => "INVALID_PARAM",
                        122 => "BUFFER_TOO_SMALL",
                        _   => ""
                    };
                    Console.ForegroundColor = lastErr == 5 ? ConsoleColor.DarkGray : ConsoleColor.Yellow;
                    Console.WriteLine($"err={lastErr} {note}");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = moreData ? ConsoleColor.Yellow : ConsoleColor.Green;
                    Console.WriteLine(moreData
                        ? $"MORE_DATA  bytesRet={ret}"
                        : $"OK  bytesRet={ret}");
                    Console.ResetColor();

                    if (ret > 0 && outSize > 0)
                    {
                        int show = (int)Math.Min(ret, (uint)outSize);
                        Console.Write($"    [{show}B]: ");
                        for (int i = 0; i < show; i++)
                        {
                            Console.Write($"{outBuf[i]:X2} ");
                            if ((i + 1) % 16 == 0) Console.Write("\n           ");
                        }
                        Console.WriteLine();
                    }
                }
            }
        }
    }

    // ── Buffer builders ───────────────────────────────────────────────────────

    /// <summary>
    /// Build the confirmed-working 23-byte write frame from a raw GIP frame.
    /// rawGip layout: [msgType, flags, seq, payloadLen, ...payload]
    /// Output layout mirrors how \\.\XboxGIP delivers upstream ReadFile frames:
    ///   [0-5]   MAC / device ID
    ///   [6-7]   00 00
    ///   [8]     MessageType  (from rawGip[0])
    ///   [9]     Flags        (from rawGip[1])
    ///   [10-11] 00 00        (seq = 0x00, bypasses dedup check)
    ///   [12-15] PayloadLen   4-byte LE  (from rawGip[3])
    ///   [16-19] 00 00 00 00
    ///   [20+]   Payload      (from rawGip[4..])
    /// </summary>
    private static byte[] BuildFrame(byte[] mac, byte[] rawGip)
    {
        byte payloadLen = rawGip[3];
        var buf = new byte[20 + payloadLen];
        Array.Copy(mac, buf, 6);
        buf[8]  = rawGip[0];                          // MessageType
        buf[9]  = rawGip[1];                          // Flags
        // [10-11] = 0x00 0x00  (seq = 0x00)
        buf[12] = payloadLen;                         // PayloadLen LE byte 0
        // [13-19] = 0x00
        Array.Copy(rawGip, 4, buf, 20, payloadLen);   // payload
        return buf;
    }

    /// <summary>
    /// Builds a 25-byte read-symmetric frame with CRC-16-CCITT appended to the payload.
    /// PayloadLen is set to rawGip[3]+2 (= 5 for LED command) so the driver accepts the size.
    /// CRC is computed over all rawGip bytes (msgType+flags+seq+payloadLen+payload).
    /// </summary>
    private static byte[] BuildFrameWithCrc(byte[] mac, byte[] rawGip, bool bigEndian)
    {
        ushort crc        = Crc16Ccitt(rawGip, 0, rawGip.Length);
        byte   origLen    = rawGip[3];
        byte   newPayLen  = (byte)(origLen + 2);
        var    buf        = new byte[20 + newPayLen];

        Array.Copy(mac, buf, 6);
        buf[8]  = rawGip[0];                           // MessageType
        buf[9]  = rawGip[1];                           // Flags
        buf[12] = newPayLen;                           // PayloadLen (5)
        Array.Copy(rawGip, 4, buf, 20, origLen);       // original payload

        int crcOffset = 20 + origLen;
        if (bigEndian)
        {
            buf[crcOffset]     = (byte)(crc >> 8);
            buf[crcOffset + 1] = (byte)(crc & 0xFF);
        }
        else
        {
            buf[crcOffset]     = (byte)(crc & 0xFF);
            buf[crcOffset + 1] = (byte)(crc >> 8);
        }

        return buf;
    }

    /// <summary>
    /// CRC-16-CCITT: poly=0x1021, init=0xFFFF, no input/output reflection (XMODEM variant).
    /// </summary>
    private static ushort Crc16Ccitt(byte[] data, int start, int count)
    {
        ushort crc = 0xFFFF;
        for (int i = start; i < start + count; i++)
        {
            crc ^= (ushort)(data[i] << 8);
            for (int j = 0; j < 8; j++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }

    private static byte[] PrefixMac(byte[] mac, byte[] gip)
    {
        var buf = new byte[8 + gip.Length];
        Array.Copy(mac, buf, 6);
        Array.Copy(gip, 0, buf, 8, gip.Length);
        return buf;
    }

    private static byte[] Pad(byte[] src, int size)
    {
        if (src.Length == size) return src;
        var buf = new byte[size];
        Array.Copy(src, buf, Math.Min(src.Length, size));
        return buf;
    }

    // ── MAC / device-ID discovery ─────────────────────────────────────────────

    private static byte[]? ReadMac(
        Microsoft.Win32.SafeHandles.SafeFileHandle hFile, int timeoutMs)
    {
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
                Console.WriteLine($"  ReadFile error {err} (0x{err:X8})");
                return null;
            }

            if (bytesRead >= 6)
                return buf[0..6];

            System.Threading.Thread.Sleep(10);
        }

        return null;
    }
}
