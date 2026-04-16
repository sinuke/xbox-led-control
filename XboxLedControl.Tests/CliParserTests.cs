using XboxLedControl;

namespace XboxLedControl.Tests;

public class CliParserTests
{
    // --- Helper: parse and assert success ---

    private static LedOptions ParseLed(params string[] args)
    {
        bool ok = CliParser.TryParse(args, out AppOptions? options, out _);
        Assert.True(ok, $"Expected TryParse to succeed for args: {string.Join(" ", args)}");
        return Assert.IsType<LedOptions>(options);
    }

    // =========================================================
    // Keyword patterns
    // =========================================================

    [Theory]
    [InlineData("off",       (int)LedCommand.Off,       0)]
    [InlineData("on",        (int)LedCommand.On,        47)]
    [InlineData("ramp",      (int)LedCommand.Ramp,      47)]
    [InlineData("fastblink", (int)LedCommand.FastBlink, 47)]
    [InlineData("slowblink", (int)LedCommand.SlowBlink, 47)]
    [InlineData("charging",  (int)LedCommand.Charging,  47)]
    public void TryParse_LedKeyword_ReturnsCorrectOptions(string keyword, int expectedPattern, byte expectedIntensity)
    {
        var opts = ParseLed("led", keyword);
        Assert.Equal((LedCommand)expectedPattern, opts.Pattern);
        Assert.Equal(expectedIntensity,           opts.Intensity);
    }

    [Theory]
    [InlineData("OFF")]
    [InlineData("On")]
    [InlineData("FASTBLINK")]
    [InlineData("Charging")]
    public void TryParse_LedKeyword_IsCaseInsensitive(string keyword)
    {
        bool ok = CliParser.TryParse(["led", keyword], out _, out _);
        Assert.True(ok);
    }

    // =========================================================
    // Numeric intensity
    // =========================================================

    [Fact]
    public void TryParse_LedZero_ReturnsOff()
    {
        var opts = ParseLed("led", "0");
        Assert.Equal(LedCommand.Off, opts.Pattern);
        Assert.Equal((byte)0,        opts.Intensity);
    }

    [Fact]
    public void TryParse_LedHundred_ReturnsMaxIntensity()
    {
        var opts = ParseLed("led", "100");
        Assert.Equal(LedCommand.On, opts.Pattern);
        Assert.Equal((byte)47,      opts.Intensity);
    }

    [Theory]
    [InlineData("1",   0)]
    [InlineData("50",  24)]
    [InlineData("99",  47)]
    public void TryParse_LedNumeric_ScalesIntensity(string value, byte expectedIntensity)
    {
        var opts = ParseLed("led", value);
        Assert.Equal(LedCommand.On,    opts.Pattern);
        Assert.Equal(expectedIntensity, opts.Intensity);
    }

    // =========================================================
    // --verbose / -v flag
    // =========================================================

    [Fact]
    public void TryParse_WithoutVerbose_VerboseIsFalse()
    {
        var opts = ParseLed("led", "on");
        Assert.False(opts.Verbose);
    }

    [Theory]
    [InlineData("--verbose")]
    [InlineData("-v")]
    public void TryParse_VerboseFlag_SetsVerboseTrue(string flag)
    {
        var opts = ParseLed("led", "on", flag);
        Assert.True(opts.Verbose);
    }

    // =========================================================
    // Invalid / unrecognised input — TryParse returns false
    // =========================================================

    [Theory]
    [InlineData("101")]
    [InlineData("-1")]
    [InlineData("200")]
    [InlineData("unknown")]
    [InlineData("blink")]   // was valid in old LedArgParser, now invalid
    [InlineData("solid")]
    [InlineData("breathe")]
    public void TryParse_InvalidLedValue_ReturnsFalse(string value)
    {
        bool ok = CliParser.TryParse(["led", value], out _, out int exitCode);
        Assert.False(ok);
        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void TryParse_NoSubcommand_ReturnsFalse()
    {
        bool ok = CliParser.TryParse([], out _, out int exitCode);
        Assert.False(ok);
        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void TryParse_LedWithNoValue_ReturnsFalse()
    {
        bool ok = CliParser.TryParse(["led"], out _, out int exitCode);
        Assert.False(ok);
        Assert.NotEqual(0, exitCode);
    }

    // =========================================================
    // --device / -d option
    // =========================================================

    [Fact]
    public void TryParse_WithoutDevice_DeviceIdIsNull()
    {
        var opts = ParseLed("led", "on");
        Assert.Null(opts.DeviceId);
    }

    [Theory]
    [InlineData("--device")]
    [InlineData("-d")]
    public void TryParse_DeviceFlag_ParsesCorrectBytes(string flag)
    {
        var opts = ParseLed("led", "on", flag, "AA:BB:CC:DD:EE:FF");
        Assert.NotNull(opts.DeviceId);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF }, opts.DeviceId);
    }

    [Fact]
    public void TryParse_DeviceFlag_LowercaseHex_Parsed()
    {
        var opts = ParseLed("led", "on", "--device", "aa:bb:cc:dd:ee:ff");
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF }, opts.DeviceId);
    }

    [Theory]
    [InlineData("AA:BB:CC:DD:EE")]        // only 5 bytes
    [InlineData("AA:BB:CC:DD:EE:FF:00")]  // 7 bytes
    [InlineData("AA:BB:CC:DD:EE:GG")]     // invalid hex
    [InlineData("AABBCCDDEEFF")]           // missing colons
    [InlineData("AA-BB-CC-DD-EE-FF")]     // wrong separator
    [InlineData("")]                       // empty
    public void TryParse_InvalidDeviceId_ReturnsFalse(string deviceId)
    {
        bool ok = CliParser.TryParse(["led", "on", "--device", deviceId], out _, out int exitCode);
        Assert.False(ok);
        Assert.NotEqual(0, exitCode);
    }

    // =========================================================
    // System.CommandLine built-ins return false with exit code 0
    // =========================================================

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    [InlineData("--version")]
    public void TryParse_BuiltInOption_ReturnsFalseWithExitZero(string flag)
    {
        bool ok = CliParser.TryParse([flag], out _, out int exitCode);
        Assert.False(ok);
        Assert.Equal(0, exitCode);
    }

    [Theory]
    [InlineData("led", "--help")]
    [InlineData("led", "-h")]
    public void TryParse_LedHelp_ReturnsFalseWithExitZero(string sub, string flag)
    {
        bool ok = CliParser.TryParse([sub, flag], out _, out int exitCode);
        Assert.False(ok);
        Assert.Equal(0, exitCode);
    }
}
