using System.Text;

namespace XboxLedControl;

/// <summary>
/// Decodes compiled GIP metadata binary blobs produced by the MetadataCompiler
/// (see §2.2.2 GIP Metadata Exchange, §2.2.2.1 GIP Metadata).
///
/// Binary layout (reverse-engineered from actual Xbox Series controller blobs):
///
///   Header (20 bytes)
///   ┌──────────────────────────────────────────────────────────────────┐
///   │ [0–1]   VendorID  (U16 LE, e.g. 0x045E = Microsoft)             │
///   │ [2–3]   ProductID (U16 LE, e.g. 0x0B12 = Xbox Series gamepad)   │
///   │ [4–7]   Flags / version (U16 + U16)                              │
///   │ [8–17]  Reserved (10 bytes)                                      │
///   │ [18–19] Total metadata size (U16 LE)                             │
///   └──────────────────────────────────────────────────────────────────┘
///
///   DeviceMetadata (starting at byte 20, prefixed by its own 2-byte size)
///   ┌──────────────────────────────────────────────────────────────────┐
///   │ [20–21] DeviceMetadata size (U16 LE)                             │
///   │                                                                  │
///   │ Offset table (12 bytes = 6 × U16 LE, all relative to byte 20)   │
///   │ [22–23] Offset 0 — SupportedDeviceFirmwareVersions               │
///   │ [24–25] Offset 1 — SupportedAudioFormats                         │
///   │ [26–27] Offset 2 — SupportedInSystemCommands                     │
///   │ [28–29] Offset 3 — SupportedOutSystemCommands                    │
///   │ [30–31] Offset 4 — PreferredTypes                                │
///   │ [32–33] Offset 5 — SupportedInterfaces                           │
///   └──────────────────────────────────────────────────────────────────┘
///
///   Variable-length data sections at their respective offsets.
///   Messages array follows the last offset-addressable section.
/// </summary>
internal static class GipMetadataDecoder
{
    private const int DEVMETA_BASE = 20;
    private const int OFFSET_TABLE_START = 22;
    private const int OFFSET_TABLE_SIZE = 12;
    private const int MIN_BLOB_SIZE = OFFSET_TABLE_START + OFFSET_TABLE_SIZE;

    /// <summary>
    /// Decode a compiled GIP metadata blob and return a human-readable summary.
    /// Returns <c>null</c> if the blob is too small to contain valid metadata.
    /// </summary>
    public static string? Decode(byte[] blob)
    {
        if (blob.Length < MIN_BLOB_SIZE)
            return null;

        var sb = new StringBuilder();
        int pos = 0;

        // --- Header ---
        ushort vendorId  = ReadU16(blob, ref pos);
        ushort productId = ReadU16(blob, ref pos);
        ushort flags     = ReadU16(blob, ref pos);
        ushort version   = ReadU16(blob, ref pos);
        pos = 18;
        _ = ReadU16(blob, ref pos);   // totalSize   — advances pos to 20
        _ = ReadU16(blob, ref pos);   // devMetaSize — advances pos to 22

        sb.AppendLine($"VendorID:   0x{vendorId:X4}{KnownVendor(vendorId)}");
        sb.AppendLine($"ProductID:  0x{productId:X4}{KnownProduct(vendorId, productId)}");
        sb.AppendLine($"Version:    {version}.{flags >> 8}");

        // --- Offset table (6 × U16 LE, all relative to DEVMETA_BASE = byte 20) ---
        pos = OFFSET_TABLE_START;
        int offFirmware  = DEVMETA_BASE + ReadU16(blob, ref pos);
        _                = DEVMETA_BASE + ReadU16(blob, ref pos);   // offAudio — not decoded
        int offInSysCmd  = DEVMETA_BASE + ReadU16(blob, ref pos);
        int offOutSysCmd = DEVMETA_BASE + ReadU16(blob, ref pos);
        int offPrefTypes = DEVMETA_BASE + ReadU16(blob, ref pos);
        int offInterfaces = DEVMETA_BASE + ReadU16(blob, ref pos);

        // --- SupportedInSystemCommands (via offset table) ---
        if (offInSysCmd < blob.Length)
        {
            pos = offInSysCmd;
            byte inCount = blob[pos++];
            if (inCount > 0 && pos + inCount <= blob.Length)
            {
                byte[] inCmds = blob[pos..(pos + inCount)];
                sb.AppendLine();
                sb.AppendLine("SupportedInSystemCommands:");
                foreach (byte cmd in inCmds)
                    sb.AppendLine($"  {cmd,3} - {SystemCommandName(cmd)}");
            }
        }

        // --- SupportedOutSystemCommands (via offset table) ---
        if (offOutSysCmd < blob.Length)
        {
            pos = offOutSysCmd;
            byte outCount = blob[pos++];
            if (outCount > 0 && pos + outCount <= blob.Length)
            {
                byte[] outCmds = blob[pos..(pos + outCount)];
                sb.AppendLine();
                sb.AppendLine("SupportedOutSystemCommands:");
                foreach (byte cmd in outCmds)
                    sb.AppendLine($"  {cmd,3} - {SystemCommandName(cmd)}");
            }
        }

        // --- SupportedDeviceFirmwareVersions (via offset table) ---
        if (offFirmware < blob.Length)
        {
            pos = offFirmware;
            byte fwCount = blob[pos++];
            if (fwCount > 0 && fwCount <= 10 && pos + fwCount * 4 <= blob.Length)
            {
                sb.AppendLine();
                sb.AppendLine("FirmwareVersions:");
                for (int i = 0; i < fwCount; i++)
                {
                    ushort major = ReadU16(blob, ref pos);
                    ushort minor = ReadU16(blob, ref pos);
                    sb.AppendLine($"  {major}.{minor}");
                }
            }
        }

        // --- PreferredTypes (via offset table) ---
        if (offPrefTypes < blob.Length)
        {
            pos = offPrefTypes;
            byte ptCount = blob[pos++];
            if (ptCount > 0 && ptCount <= 20)
            {
                sb.AppendLine();
                sb.AppendLine("PreferredTypes:");
                for (int i = 0; i < ptCount && pos + 2 <= blob.Length; i++)
                {
                    ushort strLen = ReadU16(blob, ref pos);
                    if (strLen == 0 || pos + strLen > blob.Length) break;
                    string typeName = Encoding.UTF8.GetString(blob, pos, strLen);
                    pos += strLen;
                    sb.AppendLine($"  {typeName}");
                }
            }
        }

        // --- SupportedInterfaces (via offset table) ---
        if (offInterfaces < blob.Length)
        {
            pos = offInterfaces;
            byte ifCount = blob[pos++];
            if (ifCount > 0 && ifCount <= 32 && pos + ifCount * 16 <= blob.Length)
            {
                sb.AppendLine();
                sb.AppendLine("SupportedInterfaces:");
                for (int i = 0; i < ifCount; i++)
                {
                    var guid = new Guid(blob.AsSpan(pos, 16));
                    pos += 16;
                    string gs = guid.ToString().ToUpperInvariant();
                    string name = KnownInterface(gs);
                    sb.AppendLine($"  {{{gs}}}{name}");
                }
                // pos is now right after the last interface GUID
            }
        }

        // --- Messages array (follows immediately after SupportedInterfaces data) ---
        // Compute messages start: after the last interface GUID
        int messagesPos = offInterfaces < blob.Length
            ? offInterfaces + 1 + blob[offInterfaces] * 16
            : blob.Length;

        if (messagesPos < blob.Length)
        {
            pos = messagesPos;
            byte msgCount = blob[pos++];
            if (msgCount > 0 && msgCount <= 32)
            {
                sb.AppendLine();
                sb.AppendLine("Messages:");
                for (int i = 0; i < msgCount; i++)
                {
                    if (pos + 2 > blob.Length) break;
                    ushort entrySize = ReadU16(blob, ref pos);
                    if (entrySize < 2 || pos + entrySize - 2 > blob.Length) break;

                    int entryStart = pos;
                    byte msgType = blob[pos++];
                    byte msgFlags = blob[pos++];

                    // MaxLen is at offset 5 within entry data (entryStart + 5)
                    byte msgLength = (entryStart + 5 < blob.Length) ? blob[entryStart + 5] : (byte)0;

                    string dir = MessageDirection(msgFlags);
                    sb.AppendLine($"  MsgType=0x{msgType:X2} ({msgType}), MaxLen={msgLength}, {dir}");

                    // Advance to next entry
                    pos = entryStart + entrySize - 2;
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Locate the SupportedInSystemCommands section by searching for the
    /// count byte followed by the mandatory [1, 2, 3, 4] command prefix.
    /// Every GIP device MUST list commands 1 (Protocol Control), 2 (Hello),
    /// 3 (Status), and 4 (Metadata Response) — see §2.2.2.4.1.
    /// Falls back to offset table if the heuristic scan fails.
    /// </summary>
    internal static int FindInSysCmdSection(byte[] blob)
    {
        if (blob.Length < MIN_BLOB_SIZE) return -1;

        // Primary: use offset table (entry at bytes 26-27, relative to byte 20)
        int pos = 26;
        int offset = DEVMETA_BASE + ReadU16(blob, ref pos);
        if (offset < blob.Length && blob.Length > offset + 4 &&
            blob[offset + 1] == 1 && blob[offset + 2] == 2 &&
            blob[offset + 3] == 3 && blob[offset + 4] == 4)
        {
            return offset;
        }

        // Fallback: linear scan for [count, 1, 2, 3, 4] pattern
        for (int i = MIN_BLOB_SIZE; i < blob.Length - 5; i++)
        {
            byte count = blob[i];
            if (count < 4 || count > 20) continue;
            if (i + 1 + count > blob.Length) continue;

            if (blob[i + 1] == 1 && blob[i + 2] == 2 &&
                blob[i + 3] == 3 && blob[i + 4] == 4)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Format a byte array as a hex dump with an ASCII column (xxd-style).
    /// </summary>
    internal static string FormatHexDump(byte[] data)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < data.Length; i += 16)
        {
            sb.Append($"  {i:X4}  ");
            int count = Math.Min(16, data.Length - i);
            for (int j = 0; j < 16; j++)
            {
                if (j == 8) sb.Append(' ');
                sb.Append(j < count ? $"{data[i + j]:X2} " : "   ");
            }
            sb.Append(" |");
            for (int j = 0; j < count; j++)
            {
                byte b = data[i + j];
                sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }
            sb.AppendLine("|");
        }
        return sb.ToString();
    }

    private static ushort ReadU16(byte[] data, ref int pos)
    {
        ushort val = BitConverter.ToUInt16(data, pos);
        pos += 2;
        return val;
    }

    /// <summary>
    /// Decode message direction/capability flags (§2.2.10.2).
    /// Bit 5 (0x20) = device→host, Bit 4 (0x10) = host→device,
    /// Bit 3 (0x08) = ?, Bit 2 (0x04) = ?, Bit 6 (0x40) = system/internal.
    /// </summary>
    private static string MessageDirection(byte flags)
    {
        bool up   = (flags & 0x20) != 0;
        bool down = (flags & 0x10) != 0;
        bool sys  = (flags & 0x40) != 0;

        string dir = (up, down) switch
        {
            (true, true)  => "↕ bidirectional",
            (true, false) => "↑ upstream (device→host)",
            (false, true) => "↓ downstream (host→device)",
            _             => sys ? "⚙ system" : "? unknown",
        };

        return dir;
    }

    private static string KnownVendor(ushort vid) => vid switch
    {
        0x045E => " (Microsoft)",
        _      => "",
    };

    /// <summary>Human-readable controller name, or "Xbox Controller" if unknown.</summary>
    internal static string ProductName(ushort vid, ushort pid) => (vid, pid) switch
    {
        (0x045E, 0x02D1) => "Xbox One Controller",
        (0x045E, 0x02DD) => "Xbox One Controller",
        (0x045E, 0x02E3) => "Xbox One Elite Controller",
        (0x045E, 0x02EA) => "Xbox One S Controller",
        (0x045E, 0x02FD) => "Xbox One S Controller [BT]",
        (0x045E, 0x0B00) => "Xbox Elite Series 2",
        (0x045E, 0x0B05) => "Xbox Elite Series 2 [BT]",
        (0x045E, 0x0B12) => "Xbox Series X|S Controller",
        (0x045E, 0x0B13) => "Xbox Series X|S Controller [BT]",
        (0x045E, 0x0B20) => "Xbox Adaptive Controller",
        _                => "Xbox Controller",
    };

    private static string KnownProduct(ushort vid, ushort pid)
    {
        string name = ProductName(vid, pid);
        return name == "Xbox Controller" ? "" : $" ({name})";
    }

    /// <summary>
    /// System command names per §2.2.2.4.1 and §2.2.2.4.2 (Tables 2–3).
    /// </summary>
    private static string SystemCommandName(byte cmd) => cmd switch
    {
        1  => "Protocol Control",
        2  => "Hello Device",
        3  => "Status Device",
        4  => "Metadata Request/Response",
        5  => "Set Device State",
        6  => "Security Exchange",
        7  => "Key Input (Guide button)",
        8  => "Audio Control",
        10 => "LED Commands",
        12 => "System Command 12",
        13 => "System Command 13",
        30 => "Extended Status",
        31 => "Debug Message",
        96 => "Audio Data",
        _  => $"Unknown ({cmd})",
    };

    /// <summary>
    /// Known interface GUIDs per §2.2.2.4.6.
    /// </summary>
    private static string KnownInterface(string guid) => guid switch
    {
        "082E402C-07DF-45E1-A5AB-A3127AF197B5" => " Microsoft.Xbox.Input.IGamepad",
        "31C1034D-B5B7-4551-9813-8769D4A0E4F9" => " Microsoft.Xbox.Input.IProgrammableGamepad",
        "332054CC-A34B-41D5-A34A-A6A6711EC4B3" => " Microsoft.Xbox.Input.IArcadeStick",
        "646979CF-6B71-4E96-8DF9-59E398D7420C" => " Microsoft.Xbox.Input.IWheel",
        "B8F31FE7-7386-40E9-A9F8-2F21263ACFB7" => " Windows.Xbox.Input.INavigationController",
        "9776FF56-9BFD-4581-AD45-B645BBA526D6" => " Windows.Xbox.Input.IController",
        "BC25D1A3-C24E-4992-9DDA-EF4F123EF5DC" => " Windows.Xbox.Input.IHeadset",
        "63FD9CC9-94EE-4B5D-9C4D-8B864C149CAC" => " Windows.Xbox.Input.ICustomAudio",
        "ECDDD2FE-D387-4294-BD96-1A712E3DC77D" => " Windows.Xbox.Input.IConsoleFunctionMap",
        _                                       => "",
    };
}
