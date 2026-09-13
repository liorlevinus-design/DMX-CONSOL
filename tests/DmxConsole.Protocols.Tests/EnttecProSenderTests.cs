using DmxConsole.Protocols.Usb;
using Xunit;

namespace DmxConsole.Protocols.Tests;

public class EnttecProSenderTests
{
    [Fact]
    public void BuildFrame_WrapsDataWithStartAndEndDelimiters()
    {
        var dmx = new byte[] { 10, 20, 30 };
        var frame = EnttecProSender.BuildFrame(dmx);

        Assert.Equal(0x7E, frame[0]);       // Start of Message
        Assert.Equal(6, frame[1]);          // Label: Output Only Send DMX Packet
        Assert.Equal(0xE7, frame[^1]);      // End of Message
    }

    [Fact]
    public void BuildFrame_LengthFieldCountsStartCodePlusData()
    {
        var dmx = new byte[100];
        var frame = EnttecProSender.BuildFrame(dmx);

        int length = frame[2] | (frame[3] << 8);
        Assert.Equal(101, length); // 1 start code + 100 data bytes
    }

    [Fact]
    public void BuildFrame_EmbedsDmxStartCodeThenData()
    {
        var dmx = new byte[] { 1, 2, 3 };
        var frame = EnttecProSender.BuildFrame(dmx);

        Assert.Equal(0x00, frame[4]); // DMX start code
        Assert.Equal(1, frame[5]);
        Assert.Equal(2, frame[6]);
        Assert.Equal(3, frame[7]);
    }

    [Fact]
    public void BuildFrame_TotalLengthMatchesEnvelopeMath()
    {
        var dmx = new byte[512];
        var frame = EnttecProSender.BuildFrame(dmx);

        // 4 header bytes + start code + 512 data bytes + 1 end delimiter
        Assert.Equal(4 + 1 + 512 + 1, frame.Length);
    }

    [Fact]
    public void BuildFrame_RejectsOversizedData()
    {
        var dmx = new byte[513];
        Assert.Throws<ArgumentException>(() => EnttecProSender.BuildFrame(dmx));
    }
}
