using DmxConsole.Core.Fixtures;
using Xunit;

namespace DmxConsole.Core.Tests;

/// <summary>Milestone 1 continued (2026-09-16) - FixtureChannel's calibrated display value,
/// added so the Encoder Drawer can show a real unit/range instead of always raw DMX.</summary>
public class FixtureChannelUnitTests
{
    [Fact]
    public void DefaultUnit_IsIdentityPassthrough()
    {
        var channel = new FixtureChannel { Name = "Gobo", Type = ChannelType.Gobo, Offset = 0 };

        Assert.Equal("DMX", channel.Unit);
        Assert.Equal(0.0, channel.ToDisplayValue(0));
        Assert.Equal(128.0, channel.ToDisplayValue(128));
        Assert.Equal(255.0, channel.ToDisplayValue(255));
        Assert.Equal((byte)128, channel.FromDisplayValue(128));
    }

    [Fact]
    public void PercentUnit_ConvertsRawToPercent_AndBack()
    {
        var channel = new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0, Unit = "%", MinValue = 0, MaxValue = 100 };

        Assert.Equal(0.0, channel.ToDisplayValue(0));
        Assert.Equal(100.0, channel.ToDisplayValue(255));
        Assert.Equal(50.0, channel.ToDisplayValue(128), precision: 0);

        Assert.Equal((byte)255, channel.FromDisplayValue(100));
        Assert.Equal((byte)0, channel.FromDisplayValue(0));
        Assert.Equal((byte)128, channel.FromDisplayValue(50)); // ~50%, banker's rounding of 127.5
    }

    [Fact]
    public void DegreeUnit_WithNegativeMin_ConvertsCorrectly()
    {
        var channel = new FixtureChannel { Name = "Pan", Type = ChannelType.Pan, Offset = 0, Unit = "°", MinValue = -270, MaxValue = 270 };

        Assert.Equal(-270.0, channel.ToDisplayValue(0));
        Assert.Equal(270.0, channel.ToDisplayValue(255));
        Assert.InRange(channel.ToDisplayValue(128), -1.0, 1.5); // near the midpoint (byte 128 isn't the exact center of 0..255)

        Assert.Equal((byte)0, channel.FromDisplayValue(-270));
        Assert.Equal((byte)255, channel.FromDisplayValue(270));
    }

    [Fact]
    public void FromDisplayValue_ClampsOutOfRangeInput()
    {
        var channel = new FixtureChannel { Name = "Dimmer", Type = ChannelType.Dimmer, Offset = 0, Unit = "%", MinValue = 0, MaxValue = 100 };

        Assert.Equal((byte)255, channel.FromDisplayValue(500)); // clamps to Max before converting
        Assert.Equal((byte)0, channel.FromDisplayValue(-50));   // clamps to Min before converting
    }
}
