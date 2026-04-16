using XboxLedControl;

namespace XboxLedControl.Tests;

public class GipLedCommandTests
{
    // --- BuildRaw ---

    [Fact]
    public void BuildRaw_Returns7Bytes()
    {
        var frame = GipLedCommand.BuildRaw(GipLedPattern.On, 47);
        Assert.Equal(7, frame.Length);
    }

    [Fact]
    public void BuildRaw_HeaderBytesAreFixed()
    {
        var frame = GipLedCommand.BuildRaw(GipLedPattern.On, 20);
        Assert.Equal(0x0A, frame[0]); // MessageType
        Assert.Equal(0x20, frame[1]); // Flags
        Assert.Equal(0x00, frame[2]); // SequenceId
        Assert.Equal(0x03, frame[3]); // PayloadLength
        Assert.Equal(0x00, frame[4]); // Sub-command
    }

    [Theory]
    [InlineData((byte)0x00,  0,    0x00)]  // Off
    [InlineData((byte)0x01, 47,    0x01)]  // On
    [InlineData((byte)0x02, 47,    0x02)]  // FastBlink
    [InlineData((byte)0x03, 47,    0x03)]  // SlowBlink
    [InlineData((byte)0x04, 47,    0x04)]  // ChargingBlink
    [InlineData((byte)0x0D, 47,    0x0D)]  // RampToLevel
    public void BuildRaw_EncodesPatternAndIntensity(byte patternByte, byte intensity, byte expectedPatternByte)
    {
        var frame = GipLedCommand.BuildRaw((GipLedPattern)patternByte, intensity);
        Assert.Equal(expectedPatternByte, frame[5]);
        Assert.Equal(intensity,           frame[6]);
    }

    // --- ScaleIntensity ---

    [Theory]
    [InlineData(  0,  0)]   //   0% → 0
    [InlineData(  1,  0)]   //   1% → Math.Round(0.47) = 0
    [InlineData( 50, 24)]   //  50% → Math.Round(23.5, AwayFromZero) = 24
    [InlineData( 99, 47)]   //  99% → Math.Round(46.53) = 47
    [InlineData(100, 47)]   // 100% → 47
    public void ScaleIntensity_MapsCorrectly(byte input, byte expected)
    {
        Assert.Equal(expected, GipLedCommand.ScaleIntensity(input));
    }

    [Fact]
    public void ScaleIntensity_NeverExceeds47()
    {
        for (byte i = 0; i <= 100; i++)
            Assert.True(GipLedCommand.ScaleIntensity(i) <= 47);
    }
}
