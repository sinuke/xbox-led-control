namespace XboxLedControl;

/// <summary>
/// Parses GIP Metadata response transport frames per [MS-GIPUSB] §3.1.5.5.4.
///
/// The controller broadcasts metadata automatically during enumeration; no explicit
/// request is sent (xboxgip.sys only forwards host-initiated writes for LED commands).
///
/// Response flow (§3.1.5.5.4):
///   1. Initial fragment  → Flags 0xF0 (Fragment | InitFrag | System | ACME)
///   2. Middle fragments  → Flags 0xA0 (Fragment | System)
///   3. Final fragment    → Flags 0xB0 (Fragment | System | ACME)
///   4. Complete signal   → Flags 0xA0, PayloadLen = 0
///
/// See also: §3.1.5.2 Reliable Large Message Transmission (fragment reassembly),
///           §2.2.2 GIP Metadata Exchange (metadata JSON/binary format).
/// </summary>
internal static class GipMetadataCommand
{
    /// <summary>MessageType for Metadata command (§3.1.5.5.3).</summary>
    internal const byte MSG_TYPE_METADATA = 0x04;

    // GIP Flag bits (§2.2.10.2, Table 14)
    internal const byte FLAG_FRAGMENT = 0x80;
    internal const byte FLAG_INITFRAG = 0x40;
    internal const byte FLAG_ACME     = 0x10;

    /// <summary>
    /// Attempt to parse an incoming transport frame as a metadata response fragment.
    /// Returns <c>null</c> if the frame is not a metadata message (MessageType ≠ 0x04)
    /// or the frame is too short (&lt; 20 bytes).
    ///
    /// Transport frame layout (incoming):
    ///   [0–5]   Device ID (MAC)
    ///   [6–7]   00 00
    ///   [8]     GIP MessageType
    ///   [9]     GIP Flags
    ///   [10]    SequenceId
    ///   [11]    00
    ///   [12–15] PayloadLen (4-byte LE) — fragment payload length
    ///   [16–19] TLO (4-byte LE) — Total Length (initial frag) or Offset (subsequent)
    ///   [20+]   Payload data
    /// </summary>
    internal static MetadataFragment? ParseTransportFrame(byte[] frame, int bytesRead)
    {
        if (bytesRead < 20 || frame[8] != MSG_TYPE_METADATA)
            return null;

        byte flags       = frame[9];
        uint payloadLen  = BitConverter.ToUInt32(frame, 12);
        uint tlo         = BitConverter.ToUInt32(frame, 16);

        bool fragmented = (flags & FLAG_FRAGMENT) != 0;
        bool initial    = (flags & FLAG_INITFRAG) != 0;

        int dataLen = (int)Math.Min(payloadLen, (uint)(bytesRead - 20));

        if (!fragmented)
        {
            // Non-fragmented complete response (uncommon for metadata, but handle it)
            return new MetadataFragment(FragmentType.Complete, Offset: 0,
                TotalLength: payloadLen, frame[20..(20 + dataLen)]);
        }

        if (initial)
        {
            // §3.1.5.5.4.1 — Initial fragment: TLO = total message length
            return new MetadataFragment(FragmentType.Initial, Offset: 0,
                TotalLength: tlo, frame[20..(20 + dataLen)]);
        }

        if (payloadLen == 0)
        {
            // §3.1.5.5.4.4 — Metadata Complete: PayloadLen = 0, TLO = total length
            return new MetadataFragment(FragmentType.MetadataComplete, Offset: 0,
                TotalLength: tlo, []);
        }

        // §3.1.5.5.4.2 / §3.1.5.5.4.3 — Middle or Final: TLO = offset; ACME bit marks final
        bool isFinal = (flags & FLAG_ACME) != 0;
        return new MetadataFragment(
            isFinal ? FragmentType.Final : FragmentType.Middle,
            Offset: tlo, TotalLength: 0, frame[20..(20 + dataLen)]);
    }

}

/// <summary>Fragment type within the metadata response flow (§3.1.5.5.4).</summary>
internal enum FragmentType
{
    /// <summary>§3.1.5.5.4.1 — First fragment (Flags = 0xF0).</summary>
    Initial,
    /// <summary>§3.1.5.5.4.2 — Intermediate fragment (Flags = 0xA0, PayloadLen &gt; 0).</summary>
    Middle,
    /// <summary>§3.1.5.5.4.3 — Last data-bearing fragment (Flags = 0xB0).</summary>
    Final,
    /// <summary>§3.1.5.5.4.4 — Transfer-complete handshake (Flags = 0xA0, PayloadLen = 0).</summary>
    MetadataComplete,
    /// <summary>Non-fragmented response (unlikely for metadata, but handled).</summary>
    Complete,
}

/// <summary>
/// One parsed fragment from a metadata response transport frame.
/// </summary>
/// <param name="Type">Fragment type (initial, middle, final, complete).</param>
/// <param name="Offset">Byte offset within the full metadata message (0 for initial).</param>
/// <param name="TotalLength">Total message length (only meaningful for initial and complete).</param>
/// <param name="Data">Fragment payload bytes.</param>
internal readonly record struct MetadataFragment(
    FragmentType Type, uint Offset, uint TotalLength, byte[] Data);
