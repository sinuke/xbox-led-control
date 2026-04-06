using System.ComponentModel;
using System.Runtime.InteropServices;

namespace XboxLedControl;

/// <summary>
/// Opens \\.\XboxGIP (requires xboxgip.sys — USB connection), triggers
/// re-enumeration via IOCTL, then reads whatever GIP frames the driver
/// sends back within a short window.
/// </summary>
internal static class GipDeviceLister
{
    private const uint GIP_ADD_REENUMERATE_CALLER_CONTEXT = 0x40001CD0;

    public static void List()
    {
        Console.WriteLine("Opening \\\\.\\ XboxGIP device interface...");

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
            Console.WriteLine($"  Failed to open \\\\.\\ XboxGIP — error {err} (0x{err:X8})");
            if (err == 2 || err == 3)
                Console.WriteLine("  ERROR_FILE_NOT_FOUND / ERROR_PATH_NOT_FOUND:");
                Console.WriteLine("  xboxgip.sys is not loaded — controller must be connected via USB.");
            if (err == 5)
                Console.WriteLine("  ERROR_ACCESS_DENIED: try running as Administrator.");
            Console.ResetColor();
            return;
        }

        Console.WriteLine("  Handle opened OK.");

        // Trigger re-enumeration so the driver announces connected controllers.
        bool ok = NativeMethods.DeviceIoControl(
            hFile,
            GIP_ADD_REENUMERATE_CALLER_CONTEXT,
            IntPtr.Zero, 0,
            IntPtr.Zero, 0,
            out uint bytesReturned,
            IntPtr.Zero);

        if (ok)
            Console.WriteLine($"  Re-enumeration IOCTL succeeded (bytesReturned={bytesReturned}).");
        else
        {
            int err = Marshal.GetLastWin32Error();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  Re-enumeration IOCTL failed — error {err} (0x{err:X8}), continuing to read anyway.");
            Console.ResetColor();
        }

        // Read GIP frames for up to 2 seconds.
        Console.WriteLine("\n  Reading GIP frames (up to 2 s)...\n");

        var deadline = DateTime.UtcNow.AddSeconds(2);
        var buf = new byte[1024];
        int frameCount = 0;

        while (DateTime.UtcNow < deadline)
        {
            bool readOk = NativeMethods.ReadFile(hFile, buf, (uint)buf.Length, out uint bytesRead, IntPtr.Zero);

            if (!readOk)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == 259)   // ERROR_NO_MORE_ITEMS — no data right now
                {
                    System.Threading.Thread.Sleep(50);
                    continue;
                }
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ReadFile error {err} (0x{err:X8})");
                Console.ResetColor();
                break;
            }

            if (bytesRead == 0)
            {
                System.Threading.Thread.Sleep(10);
                continue;
            }

            frameCount++;
            PrintFrame(buf, (int)bytesRead, frameCount);
        }

        if (frameCount == 0)
            Console.WriteLine("  No GIP frames received in 2 s.");
        else
            Console.WriteLine($"\n  Total frames received: {frameCount}");
    }

    // ── Frame format from \\.\XboxGIP reads ──────────────────────────────────
    //   [0-5]   Controller MAC (6 bytes)
    //   [6-7]   00 00 padding
    //   [8]     Message type
    //   [9]     Flags (always 0x20)
    //   [10-11] 00 00 reserved
    //   [12-15] Payload length (4-byte LE)
    //   [16-19] 00 00 00 00 reserved
    //   [20+]   Payload

    private static readonly Dictionary<byte, string> MsgTypes = new()
    {
        { 0x02, "DeviceInfo" },
        { 0x03, "Heartbeat"  },
        { 0x04, "Announce"   },
    };

    private static void PrintFrame(byte[] data, int length, int index)
    {
        Console.Write($"  Frame #{index:D3}  ({length} bytes)  ");

        if (length < 8)
        {
            Console.WriteLine("(too short)");
            PrintHex(data, length, 2, "    ");
            return;
        }

        string mac = MacStr(data);
        Console.Write($"MAC={mac}  ");

        if (length < 20)
        {
            Console.WriteLine("(truncated header)");
            PrintHex(data, length, 2, "    ");
            return;
        }

        byte   msgType    = data[8];
        byte   flags      = data[9];
        uint   payloadLen = ToU32(data, 12);
        string typeLabel  = MsgTypes.TryGetValue(msgType, out var lbl) ? lbl : $"0x{msgType:X2}";

        Console.WriteLine($"{typeLabel}  flags=0x{flags:X2}  payloadLen={payloadLen}");

        int actual = (int)Math.Min(payloadLen, (uint)Math.Max(0, length - 20));
        if (actual > 0)
        {
            PrintHex(data, actual, 20, "    ");

            if (msgType == 0x04)
                DecodeAnnounce(data, 20, actual);
        }
    }

    // Extract controller MAC from first 6 bytes of any frame.
    internal static byte[]? ExtractMac(byte[] data, int length)
        => length >= 6 ? data[0..6] : null;

    private static string MacStr(byte[] data)
        => $"{data[0]:X2}:{data[1]:X2}:{data[2]:X2}:{data[3]:X2}:{data[4]:X2}:{data[5]:X2}";

    private static uint ToU32(byte[] d, int offset)
        => (uint)(d[offset] | (d[offset+1] << 8) | (d[offset+2] << 16) | (d[offset+3] << 24));

    private static void PrintHex(byte[] data, int count, int startOffset, string indent)
    {
        Console.Write(indent);
        for (int i = 0; i < count; i++)
        {
            Console.Write($"{data[startOffset + i]:X2} ");
            if ((i + 1) % 16 == 0 && i + 1 < count)
                Console.Write($"\n{indent}");
        }
        Console.WriteLine();
    }

    // Look for ASCII strings (len-prefixed with 2-byte LE length) in Announce payload.
    private static void DecodeAnnounce(byte[] data, int payloadStart, int payloadLen)
    {
        // VID/PID at payload offset 0 (4 bytes)
        if (payloadLen >= 4)
        {
            ushort vid = (ushort)(data[payloadStart]     | (data[payloadStart+1] << 8));
            ushort pid = (ushort)(data[payloadStart+2]   | (data[payloadStart+3] << 8));
            Console.WriteLine($"      VID=0x{vid:X4} PID=0x{pid:X4}");
        }

        // Scan for 2-byte LE length-prefixed ASCII strings
        int end = payloadStart + payloadLen;
        for (int i = payloadStart; i + 3 < end; i++)
        {
            ushort slen = (ushort)(data[i] | (data[i+1] << 8));
            if (slen < 4 || slen > 128 || i + 2 + slen > end)
                continue;

            bool allAscii = true;
            for (int j = 0; j < slen; j++)
            {
                byte b = data[i + 2 + j];
                if (b < 0x20 || b > 0x7E) { allAscii = false; break; }
            }

            if (allAscii)
            {
                string s = System.Text.Encoding.ASCII.GetString(data, i + 2, slen);
                Console.WriteLine($"      String(len={slen}): \"{s}\"");
            }
        }
    }
}
