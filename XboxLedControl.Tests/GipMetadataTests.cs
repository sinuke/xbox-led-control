using XboxLedControl;

namespace XboxLedControl.Tests;

// ---------------------------------------------------------------------------
// Real 295-byte metadata blob captured from an Xbox Series X|S controller
// (VID=0x045E, PID=0x0B12, firmware 5.23).
// ---------------------------------------------------------------------------
file static class Blobs
{
    internal static readonly byte[] XboxSeries =
    [
        0x5E, 0x04, 0x12, 0x0B, 0x10, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x23, 0x01, 0xCD, 0x00, 0x16, 0x00, 0x1B, 0x00, 0x1C, 0x00, 0x26, 0x00, 0x2F, 0x00,
        0x4C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x05, 0x00, 0x17, 0x00, 0x00,
        0x09, 0x01, 0x02, 0x03, 0x04, 0x06, 0x07, 0x0C, 0x0D, 0x1E, 0x08, 0x01, 0x04, 0x05, 0x06, 0x0A,
        0x0C, 0x0D, 0x1E, 0x01, 0x1A, 0x00, 0x57, 0x69, 0x6E, 0x64, 0x6F, 0x77, 0x73, 0x2E, 0x58, 0x62,
        0x6F, 0x78, 0x2E, 0x49, 0x6E, 0x70, 0x75, 0x74, 0x2E, 0x47, 0x61, 0x6D, 0x65, 0x70, 0x61, 0x64,
        0x08, 0x56, 0xFF, 0x76, 0x97, 0xFD, 0x9B, 0x81, 0x45, 0xAD, 0x45, 0xB6, 0x45, 0xBB, 0xA5, 0x26,
        0xD6, 0x2C, 0x40, 0x2E, 0x08, 0xDF, 0x07, 0xE1, 0x45, 0xA5, 0xAB, 0xA3, 0x12, 0x7A, 0xF1, 0x97,
        0xB5, 0xE7, 0x1F, 0xF3, 0xB8, 0x86, 0x73, 0xE9, 0x40, 0xA9, 0xF8, 0x2F, 0x21, 0x26, 0x3A, 0xCF,
        0xB7, 0xFE, 0xD2, 0xDD, 0xEC, 0x87, 0xD3, 0x94, 0x42, 0xBD, 0x96, 0x1A, 0x71, 0x2E, 0x3D, 0xC7,
        0x7D, 0x6B, 0xE5, 0xF2, 0x87, 0xBB, 0xC3, 0xB1, 0x49, 0x82, 0x65, 0xFF, 0xFF, 0xF3, 0x77, 0x99,
        0xEE, 0x1E, 0x9B, 0xAD, 0x34, 0xAD, 0x36, 0xB5, 0x4F, 0x8A, 0xC7, 0x17, 0x23, 0x4C, 0x9F, 0x54,
        0x6F, 0x77, 0xCE, 0x34, 0x7A, 0xE2, 0x7D, 0xC6, 0x45, 0x8C, 0xA4, 0x00, 0x42, 0xC0, 0x8B, 0xD9,
        0x4A, 0xC0, 0xC8, 0x96, 0xEA, 0x16, 0xB2, 0x8B, 0x44, 0xBE, 0x80, 0x7E, 0x5D, 0xEB, 0x06, 0x98,
        0xE2, 0x03, 0x17, 0x00, 0x20, 0x2C, 0x00, 0x01, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x17, 0x00, 0x09, 0x3C, 0x00, 0x01, 0x00,
        0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x17, 0x00, 0x1E, 0x40, 0x00, 0x01, 0x00, 0x22, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];
}

// ===========================================================================
// GipMetadataDecoder tests
// ===========================================================================

public class GipMetadataDecoderTests
{
    // --- Decode: sanity / too-short ---

    [Fact]
    public void Decode_TooShort_ReturnsNull()
    {
        Assert.Null(GipMetadataDecoder.Decode(new byte[5]));
    }

    [Fact]
    public void Decode_RealBlob_ReturnsNonNull()
    {
        Assert.NotNull(GipMetadataDecoder.Decode(Blobs.XboxSeries));
    }

    // --- Decode: header fields ---

    [Fact]
    public void Decode_RealBlob_VendorAndProduct()
    {
        string output = GipMetadataDecoder.Decode(Blobs.XboxSeries)!;
        Assert.Contains("VendorID:   0x045E (Microsoft)", output);
        Assert.Contains("ProductID:  0x0B12 (Xbox Series X|S Controller)", output);
    }

    [Fact]
    public void Decode_RealBlob_Version()
    {
        string output = GipMetadataDecoder.Decode(Blobs.XboxSeries)!;
        Assert.Contains("Version:    1.0", output);
    }

    // --- Decode: SupportedInSystemCommands ---

    [Theory]
    [InlineData(1,  "Protocol Control")]
    [InlineData(2,  "Hello Device")]
    [InlineData(3,  "Status Device")]
    [InlineData(4,  "Metadata Request/Response")]
    [InlineData(6,  "Security Exchange")]
    [InlineData(7,  "Key Input (Guide button)")]
    [InlineData(12, "System Command 12")]
    [InlineData(13, "System Command 13")]
    [InlineData(30, "Extended Status")]
    public void Decode_RealBlob_InSystemCommandPresent(int cmd, string name)
    {
        string output = GipMetadataDecoder.Decode(Blobs.XboxSeries)!;
        Assert.Contains($"  {cmd,3} - {name}", output);
    }

    // --- Decode: SupportedOutSystemCommands ---

    [Theory]
    [InlineData(1,  "Protocol Control")]
    [InlineData(4,  "Metadata Request/Response")]
    [InlineData(5,  "Set Device State")]
    [InlineData(6,  "Security Exchange")]
    [InlineData(10, "LED Commands")]
    [InlineData(12, "System Command 12")]
    [InlineData(13, "System Command 13")]
    [InlineData(30, "Extended Status")]
    public void Decode_RealBlob_OutSystemCommandPresent(int cmd, string name)
    {
        string output = GipMetadataDecoder.Decode(Blobs.XboxSeries)!;
        // OutSystemCommands section appears after InSystemCommands in output
        int outSection = output.IndexOf("SupportedOutSystemCommands:", StringComparison.Ordinal);
        Assert.True(outSection >= 0);
        Assert.Contains($"  {cmd,3} - {name}", output[outSection..]);
    }

    // --- Decode: FirmwareVersions ---

    [Fact]
    public void Decode_RealBlob_FirmwareVersion()
    {
        string output = GipMetadataDecoder.Decode(Blobs.XboxSeries)!;
        Assert.Contains("FirmwareVersions:", output);
        Assert.Contains("5.23", output);
    }

    // --- Decode: PreferredTypes ---

    [Fact]
    public void Decode_RealBlob_PreferredType()
    {
        string output = GipMetadataDecoder.Decode(Blobs.XboxSeries)!;
        Assert.Contains("Windows.Xbox.Input.Gamepad", output);
    }

    // --- Decode: SupportedInterfaces ---

    [Theory]
    [InlineData("9776FF56-9BFD-4581-AD45-B645BBA526D6", "Windows.Xbox.Input.IController")]
    [InlineData("082E402C-07DF-45E1-A5AB-A3127AF197B5", "Microsoft.Xbox.Input.IGamepad")]
    [InlineData("B8F31FE7-7386-40E9-A9F8-2F21263ACFB7", "Windows.Xbox.Input.INavigationController")]
    [InlineData("ECDDD2FE-D387-4294-BD96-1A712E3DC77D", "Windows.Xbox.Input.IConsoleFunctionMap")]
    public void Decode_RealBlob_KnownInterfacePresent(string guid, string name)
    {
        string output = GipMetadataDecoder.Decode(Blobs.XboxSeries)!;
        Assert.Contains(guid, output);
        Assert.Contains(name, output);
    }

    // --- Decode: Messages ---

    [Fact]
    public void Decode_RealBlob_Messages()
    {
        string output = GipMetadataDecoder.Decode(Blobs.XboxSeries)!;
        Assert.Contains("Messages:", output);
        Assert.Contains("MsgType=0x20 (32), MaxLen=16, ↑ upstream (device→host)", output);
        Assert.Contains("MsgType=0x09 (9), MaxLen=8, ↕ bidirectional", output);
        Assert.Contains("MsgType=0x1E (30), MaxLen=34, ⚙ system", output);
    }

    // --- FindInSysCmdSection ---

    [Fact]
    public void FindInSysCmdSection_RealBlob_ReturnsOffset48()
    {
        // Offset table entry [26-27] = 0x001C → DEVMETA_BASE(20) + 28 = 48
        Assert.Equal(48, GipMetadataDecoder.FindInSysCmdSection(Blobs.XboxSeries));
    }

    [Fact]
    public void FindInSysCmdSection_TooShort_ReturnsMinusOne()
    {
        Assert.Equal(-1, GipMetadataDecoder.FindInSysCmdSection(new byte[5]));
    }

    // --- FormatHexDump ---

    [Fact]
    public void FormatHexDump_Empty_ReturnsEmptyString()
    {
        Assert.Equal("", GipMetadataDecoder.FormatHexDump([]));
    }

    [Fact]
    public void FormatHexDump_SingleByte_ContainsAddressAndHex()
    {
        string dump = GipMetadataDecoder.FormatHexDump([0x41]);
        Assert.Contains("0000", dump);
        Assert.Contains("41", dump);
        Assert.Contains("|A|", dump);
    }

    [Fact]
    public void FormatHexDump_17Bytes_ProducesTwoLines()
    {
        string dump = GipMetadataDecoder.FormatHexDump(new byte[17]);
        // Second line starts at offset 0x0010
        Assert.Contains("0000", dump);
        Assert.Contains("0010", dump);
    }

    [Fact]
    public void FormatHexDump_NonPrintable_ShowsDot()
    {
        string dump = GipMetadataDecoder.FormatHexDump([0x01]);
        Assert.Contains("|.|", dump);
    }

    // --- ProductName ---

    [Theory]
    [InlineData(0x045E, 0x0B12, "Xbox Series X|S Controller")]
    [InlineData(0x045E, 0x0B00, "Xbox Elite Series 2")]
    [InlineData(0x045E, 0x02E3, "Xbox One Elite Controller")]
    [InlineData(0x045E, 0x02EA, "Xbox One S Controller")]
    [InlineData(0x045E, 0x0B20, "Xbox Adaptive Controller")]
    [InlineData(0x045E, 0x0000, "Xbox Controller")]   // unknown PID → fallback
    [InlineData(0x0000, 0x0000, "Xbox Controller")]   // unknown VID → fallback
    public void ProductName_KnownAndUnknown(ushort vid, ushort pid, string expected)
    {
        Assert.Equal(expected, GipMetadataDecoder.ProductName(vid, pid));
    }
}

// ===========================================================================
// GipMetadataCommand.ParseTransportFrame tests
// ===========================================================================

public class GipMetadataCommandParseTests
{
    // Builds a minimal 20-byte transport frame (+ optional payload).
    private static byte[] MakeFrame(byte msgType, byte flags, uint payloadLen, uint tlo, byte[] payload)
    {
        var frame = new byte[20 + payload.Length];
        frame[8]  = msgType;
        frame[9]  = flags;
        BitConverter.TryWriteBytes(frame.AsSpan(12), payloadLen);
        BitConverter.TryWriteBytes(frame.AsSpan(16), tlo);
        payload.AsSpan().CopyTo(frame.AsSpan(20));
        return frame;
    }

    [Fact]
    public void Parse_TooShortFrame_ReturnsNull()
    {
        Assert.Null(GipMetadataCommand.ParseTransportFrame(new byte[19], 19));
    }

    [Fact]
    public void Parse_WrongMessageType_ReturnsNull()
    {
        var frame = MakeFrame(0x0A, 0x20, 0, 0, []);
        Assert.Null(GipMetadataCommand.ParseTransportFrame(frame, frame.Length));
    }

    [Fact]
    public void Parse_NonFragmented_ReturnsComplete()
    {
        byte[] payload = [0x01, 0x02];
        var frame = MakeFrame(0x04, 0x20, 2, 0, payload);
        var frag = GipMetadataCommand.ParseTransportFrame(frame, frame.Length);
        Assert.NotNull(frag);
        Assert.Equal(FragmentType.Complete, frag!.Value.Type);
        Assert.Equal(2u, frag.Value.TotalLength);
        Assert.Equal(0u, frag.Value.Offset);
        Assert.Equal(payload, frag.Value.Data);
    }

    [Fact]
    public void Parse_InitialFragment_0xF0()
    {
        byte[] payload = new byte[10];
        var frame = MakeFrame(0x04, 0xF0, (uint)payload.Length, 300u, payload);
        var frag = GipMetadataCommand.ParseTransportFrame(frame, frame.Length);
        Assert.NotNull(frag);
        Assert.Equal(FragmentType.Initial, frag!.Value.Type);
        Assert.Equal(300u, frag.Value.TotalLength);   // TLO = total length
        Assert.Equal(0u,   frag.Value.Offset);
        Assert.Equal(payload.Length, frag.Value.Data.Length);
    }

    [Fact]
    public void Parse_MiddleFragment_0xA0()
    {
        byte[] payload = new byte[58];
        var frame = MakeFrame(0x04, 0xA0, (uint)payload.Length, 58u, payload);
        var frag = GipMetadataCommand.ParseTransportFrame(frame, frame.Length);
        Assert.NotNull(frag);
        Assert.Equal(FragmentType.Middle, frag!.Value.Type);
        Assert.Equal(58u, frag.Value.Offset);   // TLO = offset
    }

    [Fact]
    public void Parse_FinalFragment_0xB0()
    {
        byte[] payload = new byte[20];
        var frame = MakeFrame(0x04, 0xB0, (uint)payload.Length, 116u, payload);
        var frag = GipMetadataCommand.ParseTransportFrame(frame, frame.Length);
        Assert.NotNull(frag);
        Assert.Equal(FragmentType.Final, frag!.Value.Type);
        Assert.Equal(116u, frag.Value.Offset);
    }

    [Fact]
    public void Parse_MetadataComplete_0xA0_ZeroPayload()
    {
        var frame = MakeFrame(0x04, 0xA0, 0, 295u, []);
        var frag = GipMetadataCommand.ParseTransportFrame(frame, frame.Length);
        Assert.NotNull(frag);
        Assert.Equal(FragmentType.MetadataComplete, frag!.Value.Type);
        Assert.Equal(295u, frag.Value.TotalLength);
        Assert.Empty(frag.Value.Data);
    }

    [Fact]
    public void Parse_BytesReadShorterThanPayload_DataClampedToAvailable()
    {
        // Report only 21 bytes read even though frame has more space
        byte[] payload = new byte[10];
        var frame = MakeFrame(0x04, 0x20, 10, 0, payload);
        var frag = GipMetadataCommand.ParseTransportFrame(frame, 21); // 21 - 20 = 1 byte of data
        Assert.NotNull(frag);
        Assert.Single(frag!.Value.Data);
    }
}

// ===========================================================================
// CliParser — metadata subcommand tests
// ===========================================================================

public class CliParserMetadataTests
{
    [Fact]
    public void TryParse_Metadata_ReturnsMetadataOptions()
    {
        bool ok = CliParser.TryParse(["metadata"], out AppOptions? options, out _);
        Assert.True(ok);
        Assert.IsType<MetadataOptions>(options);
    }

    [Fact]
    public void TryParse_Metadata_DefaultsToNoDeviceNoVerbose()
    {
        bool ok = CliParser.TryParse(["metadata"], out AppOptions? options, out _);
        Assert.True(ok);
        var opts = Assert.IsType<MetadataOptions>(options);
        Assert.Null(opts.DeviceId);
        Assert.False(opts.Verbose);
    }

    [Theory]
    [InlineData("--verbose")]
    [InlineData("-v")]
    public void TryParse_MetadataVerbose_SetsVerboseTrue(string flag)
    {
        bool ok = CliParser.TryParse(["metadata", flag], out AppOptions? options, out _);
        Assert.True(ok);
        Assert.True(Assert.IsType<MetadataOptions>(options).Verbose);
    }

    [Theory]
    [InlineData("--device")]
    [InlineData("-d")]
    public void TryParse_MetadataDevice_ParsesBytes(string flag)
    {
        bool ok = CliParser.TryParse(["metadata", flag, "AA:BB:CC:DD:EE:FF"], out AppOptions? options, out _);
        Assert.True(ok);
        var opts = Assert.IsType<MetadataOptions>(options);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF }, opts.DeviceId);
    }

    [Theory]
    [InlineData("metadata", "--help")]
    [InlineData("metadata", "-h")]
    [InlineData("metadata", "-?")]
    public void TryParse_MetadataHelp_ReturnsFalseExitZero(string sub, string flag)
    {
        bool ok = CliParser.TryParse([sub, flag], out _, out int exitCode);
        Assert.False(ok);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void TryParse_MetadataInvalidDevice_ReturnsFalse()
    {
        bool ok = CliParser.TryParse(["metadata", "--device", "not-a-mac"], out _, out int exitCode);
        Assert.False(ok);
        Assert.NotEqual(0, exitCode);
    }
}
