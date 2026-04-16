using XboxLedControl;

namespace XboxLedControl.Tests;

public class GipDirectSenderTests
{
    private static readonly byte[] SampleMac   = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF];
    private static readonly byte[] ZeroMac     = new byte[6];

    // --- Frame size ---

    [Fact]
    public void BuildFrame_SizeIs20PlusPayloadLen()
    {
        var rawGip = GipLedCommand.BuildRaw(GipLedPattern.On, 47);  // payloadLen = 3
        var frame  = GipDirectSender.BuildFrame(SampleMac, rawGip);
        Assert.Equal(23, frame.Length); // 20 + 3
    }

    // --- Device ID ---

    [Fact]
    public void BuildFrame_DeviceIdCopiedToBytes0to5()
    {
        var rawGip = GipLedCommand.BuildRaw(GipLedPattern.On, 47);
        var frame  = GipDirectSender.BuildFrame(SampleMac, rawGip);
        Assert.Equal(SampleMac, frame[0..6]);
    }

    [Fact]
    public void BuildFrame_Bytes6and7AreZero()
    {
        var rawGip = GipLedCommand.BuildRaw(GipLedPattern.On, 47);
        var frame  = GipDirectSender.BuildFrame(SampleMac, rawGip);
        Assert.Equal(0x00, frame[6]);
        Assert.Equal(0x00, frame[7]);
    }

    // --- GIP header at offset 8 ---

    [Fact]
    public void BuildFrame_GipHeaderAtOffset8()
    {
        var rawGip = GipLedCommand.BuildRaw(GipLedPattern.FastBlink, 30);
        var frame  = GipDirectSender.BuildFrame(ZeroMac, rawGip);
        Assert.Equal(0x0A, frame[8]);   // MessageType
        Assert.Equal(0x20, frame[9]);   // Flags
        Assert.Equal(0x00, frame[10]);  // SequenceId
        Assert.Equal(0x00, frame[11]);  // padding
        Assert.Equal(0x03, frame[12]);  // PayloadLen
    }

    // --- Payload at offset 20 ---

    [Theory]
    [InlineData((byte)0x00,  0)]  // Off
    [InlineData((byte)0x01, 47)]  // On
    [InlineData((byte)0x02, 20)]  // FastBlink
    [InlineData((byte)0x0D, 35)]  // RampToLevel
    public void BuildFrame_PayloadAtOffset20(byte patternByte, byte intensity)
    {
        var rawGip = GipLedCommand.BuildRaw((GipLedPattern)patternByte, intensity);
        var frame  = GipDirectSender.BuildFrame(ZeroMac, rawGip);
        Assert.Equal(0x00,        frame[20]); // sub-command
        Assert.Equal(patternByte, frame[21]); // pattern
        Assert.Equal(intensity,   frame[22]); // intensity
    }
}
