using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace XboxLedControl;

/// <summary>
/// Sends GIP LED commands via a raw Bluetooth L2CAP socket, bypassing
/// the Windows HID driver (BTHXHID.sys) entirely.
///
/// Why this is needed
/// ─────────────────
/// Xbox Series X/S BT controller exposes a single HID interface (ig_00)
/// with no output or feature reports (input=16, output=0, feature=0).
/// BTHXHID.sys creates an input-only HID node and handles the BT connection
/// itself. To send LED commands we must go below the HID layer and write
/// directly to the Bluetooth interrupt channel.
///
/// BT HID output report format (interrupt channel, PSM 0x0013)
/// ────────────────────────────────────────────────────────────
///   Byte 0 : 0xA2  = HID DATA (0x0A << 4) | OUTPUT direction (0x02)
///   Byte 1+: GIP frame (WITHOUT the Windows 0x00 report-ID prefix byte)
///
/// Windows BT socket notes
/// ───────────────────────
/// AF_BTH = 32, SOCK_STREAM, BTHPROTO_L2CAP = 0x0100
/// SOCKADDR_BTH.port = L2CAP PSM
///
/// If BTHXHID.sys already holds PSM 0x0013 on this device, a second
/// L2CAP connection may be refused by the controller (error 10061).
/// In that case the BTHXHID channel must be released first — or we
/// need another approach (e.g. WinUSB + libusbK, or Xbox Accessories app
/// private COM API).
/// </summary>
public static class BtSocketSender
{
    private const int    AF_BTH              = 32;
    private const int    SOCK_STREAM         = 1;
    private const int    BTHPROTO_L2CAP      = 0x0100;
    private const ushort HID_PSM_INTERRUPT   = 0x0013;
    private const byte   HID_OUTPUT_HEADER   = 0xA2;   // DATA | OUTPUT

    [StructLayout(LayoutKind.Sequential)]
    private struct SOCKADDR_BTH
    {
        public ushort addressFamily;   // AF_BTH
        public ulong  btAddr;          // 6-byte address packed in ULONGLONG
        public Guid   serviceClassId;  // Guid.Empty for L2CAP
        public uint   port;            // L2CAP PSM
    }

    // ── WinSock2 P/Invoke ─────────────────────────────────────────────────

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int WSAStartup(ushort wVersionRequested,
        [Out] byte[] lpWSAData);

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern IntPtr socket(int af, int type, int protocol);

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int connect(IntPtr s,
        ref SOCKADDR_BTH name, int namelen);

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int send(IntPtr s,
        [In] byte[] buf, int len, int flags);

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int closesocket(IntPtr s);

    [DllImport("ws2_32.dll")]
    private static extern int WSAGetLastError();

    private static readonly IntPtr INVALID_SOCKET = new IntPtr(-1);
    private static bool _wsaReady;

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to send <paramref name="gipCommand"/> (a GIP LED frame as
    /// produced by <see cref="GipLedCommand"/>) to the controller over
    /// a raw L2CAP socket.
    /// </summary>
    public static bool TrySend(XboxDevice device, byte[] gipCommand)
    {
        ulong btAddr = ExtractBtAddress(device.Path);
        if (btAddr == 0)
        {
            Console.WriteLine("  [FAIL] Cannot parse BT address from device path.");
            return false;
        }

        Console.WriteLine($"  BT address : {btAddr:X12}");
        Console.WriteLine($"  PSM        : 0x{HID_PSM_INTERRUPT:X4} (HID interrupt)");

        EnsureWsa();

        IntPtr sock = socket(AF_BTH, SOCK_STREAM, BTHPROTO_L2CAP);
        if (sock == INVALID_SOCKET)
        {
            Console.WriteLine($"  [FAIL] socket() failed: WSA {WSAGetLastError()}");
            return false;
        }

        try
        {
            var addr = new SOCKADDR_BTH
            {
                addressFamily  = AF_BTH,
                btAddr         = btAddr,
                serviceClassId = Guid.Empty,
                port           = HID_PSM_INTERRUPT,
            };

            Console.Write("  Connecting L2CAP ... ");
            int rc = connect(sock, ref addr, Marshal.SizeOf<SOCKADDR_BTH>());
            if (rc != 0)
            {
                int wsaErr = WSAGetLastError();
                Console.WriteLine($"FAIL  (WSA {wsaErr})");
                PrintWsaHint(wsaErr);
                return false;
            }
            Console.WriteLine("OK");

            // Build the BT HID packet:
            //   [0xA2] [GIP frame, stripping the Win32 report-ID prefix byte]
            // gipCommand[0] = 0x00 (report-ID placeholder for Win32 API only)
            // GIP frame = gipCommand[1..]
            byte[] packet = new byte[1 + (gipCommand.Length - 1)];
            packet[0] = HID_OUTPUT_HEADER;
            Array.Copy(gipCommand, 1, packet, 1, gipCommand.Length - 1);

            Console.WriteLine($"  Packet     : {BitConverter.ToString(packet)}");

            int sent = send(sock, packet, packet.Length, 0);
            if (sent > 0)
            {
                Console.WriteLine($"  [OK]  Sent {sent} bytes via L2CAP");
                return true;
            }

            Console.WriteLine($"  [FAIL] send() failed: WSA {WSAGetLastError()}");
            return false;
        }
        finally
        {
            closesocket(sock);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the 6-byte Bluetooth MAC address from a Windows device path.
    /// Example path segment: …&a88c3e3ded89&… → 0xA88C3E3DED89
    /// </summary>
    private static ulong ExtractBtAddress(string path)
    {
        // Look for a 12-char hex segment that looks like a BT MAC address.
        // In Xbox Series BT paths it appears between '&' delimiters.
        var match = Regex.Match(path, @"&([0-9a-f]{12})(?:[&#]|$)",
            RegexOptions.IgnoreCase);

        if (!match.Success) return 0;

        try { return Convert.ToUInt64(match.Groups[1].Value, 16); }
        catch { return 0; }
    }

    private static void EnsureWsa()
    {
        if (_wsaReady) return;
        WSAStartup(0x0202, new byte[408]); // Request WinSock 2.2
        _wsaReady = true;
    }

    private static void PrintWsaHint(int wsaErr)
    {
        string hint = wsaErr switch
        {
            10013 => "Access denied — try running as Administrator.",
            10048 => "Address in use (PSM owned by another connection).",
            10049 => "Address not available.",
            10061 => "Connection refused by the controller.\n" +
                     "         BTHXHID.sys may already hold this L2CAP channel.\n" +
                     "         Try: disconnect the controller from Windows BT settings,\n" +
                     "         reconnect, and run this tool before other apps claim the channel.",
            10060 => "Connection timed out.",
            10065 => "No route to host — controller not in range or turned off.",
            _     => $"See https://learn.microsoft.com/en-us/windows/win32/winsock/windows-sockets-error-codes-2 for WSA {wsaErr}",
        };
        Console.WriteLine($"         Hint: {hint}");
    }
}
