namespace XboxLedControl;

/// <summary>
/// LED patterns for the GIP Guide Button LED command.
///
/// Values match the spec exactly (§3.1.5.5.7, Table 42):
///   https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gipusb/ec312389-2e05-4915-85ed-0e8fe9c3d33b
/// </summary>
public enum GipLedPattern : byte
{
    Off           = 0x00,   // LED off
    On            = 0x01,   // Solid on (static); "Connected to host via USB"
    FastBlink     = 0x02,   // 200 ms on / 400 ms cycle
    SlowBlink     = 0x03,   // 600 ms on / 1200 ms cycle
    ChargingBlink = 0x04,   // 3000 ms on / 6000 ms cycle
    RampToLevel   = 0x0D,   // Animate / ramp up to the specified intensity level
}

/// <summary>
/// Builds the GIP Guide Button LED command per [MS-GIPUSB] §3.1.5.5.7 (Table 41).
///
/// GIP frame (7 bytes — the payload the device receives):
///
///   Message Header  (4 bytes, §2.2.10, Table 12)
///   ┌──────────────────────────────────────────────────────────────────┐
///   │ Byte 0 │ MessageType = 0x0A  (Command class bits7:5=000, msg #10)│
///   │ Byte 1 │ Flags       = 0x20  (System=1, no ACK, primary device)  │
///   │ Byte 2 │ SequenceId           wrapping 0x01–0xFF (0x00 reserved) │
///   │ Byte 3 │ PayloadLen  = 0x03  (3 bytes)                           │
///   └──────────────────────────────────────────────────────────────────┘
///
///   Guide Button LED payload  (3 bytes, Table 41)
///   ┌──────────────────────────────────────────────────────────────────┐
///   │ Byte 4 │ Sub-command = 0x00  (Guide Button LED)                  │
///   │ Byte 5 │ Pattern     (GipLedPattern enum)                        │
///   │ Byte 6 │ Intensity   0–47 (%)                                    │
///   └──────────────────────────────────────────────────────────────────┘
///
/// HID output report (8 bytes): prepends a 0x00 report-ID byte required by
/// HidD_SetOutputReport / WriteFile on HID devices (stripped by driver).
///
/// Flags = 0x20 decoded (§2.2.10.2, Table 14):
///   bit 7 (Fragment)       = 0  → single packet, not fragmented
///   bit 6 (InitFrag)       = 0  → N/A
///   bit 5 (System)         = 1  → system message, no Metadata declaration needed
///   bit 4 (ACK required)   = 0  → no acknowledgement required
///   bit 3 (Reserved)       = 0
///   bits 2:0 (Expansion)   = 000 → primary device
/// </summary>
public static class GipLedCommand
{
    private static byte _sequence = 1;

    private static byte NextSeq()
    {
        byte s = _sequence;
        _sequence = _sequence == 0xFF ? (byte)1 : (byte)(_sequence + 1);
        return s;
    }

    /// <summary>
    /// Raw 7-byte GIP frame — no HID report-ID prefix.
    /// Use for WriteFile to \\.\XboxGIP or WinUSB bulk-OUT endpoint.
    /// </summary>
    public static byte[] BuildRaw(GipLedPattern pattern, byte intensity)
    {
        if (intensity > 47)
            throw new ArgumentOutOfRangeException(nameof(intensity), "0–47 per MS-GIPUSB §3.1.5.5.7");

        return
        [
            0x0A,           // MessageType: Command class (bits 7:5 = 000), message #10 (bits 4:0 = 01010)
            0x20,           // Flags: System=1, no ACK, not fragmented, primary device
            0x00,           // SequenceId = 0x00 (bypasses driver deduplication check in read-symmetric layout)
            0x03,           // PayloadLength = 3 bytes
            0x00,           // Sub-command = 0x00 (Guide Button LED)
            (byte)pattern,  // LED pattern (Table 42)
            intensity,      // Intensity 0–47 %
        ];
    }

    /// <summary>
    /// 8-byte HID output report (0x00 report-ID + 7-byte GIP frame).
    /// Use for HidD_SetOutputReport / HidD_SetFeature.
    /// </summary>
    public static byte[] Build(GipLedPattern pattern, byte intensity)
    {
        var raw = BuildRaw(pattern, intensity);
        var buf = new byte[1 + raw.Length];   // buf[0] = 0x00 report-ID prefix
        raw.CopyTo(buf, 1);
        return buf;
    }

    public static byte[] BuildOff()
        => Build(GipLedPattern.Off, 0);

    /// <summary>intensity 0–47</summary>
    public static byte[] BuildSolid(byte intensity)
        => intensity == 0
            ? BuildOff()
            : Build(GipLedPattern.On, Math.Min(intensity, (byte)47));

    /// <summary>
    /// Scale a user-facing 0–100 brightness value to the 0–47 intensity
    /// range defined in the spec, preserving 0 → Off and 100 → max.
    /// </summary>
    public static byte ScaleIntensity(byte brightness100)
        => (byte)Math.Round(brightness100 * 47.0 / 100.0);
}
