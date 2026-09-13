using Xunit;

namespace DmxConsole.Core.Tests;

public class AttributeClassTests
{
    [Theory]
    [InlineData(ChannelType.Dimmer, AttributeClass.Intensity)]
    [InlineData(ChannelType.Pan, AttributeClass.Position)]
    [InlineData(ChannelType.PanFine, AttributeClass.Position)]
    [InlineData(ChannelType.Tilt, AttributeClass.Position)]
    [InlineData(ChannelType.TiltFine, AttributeClass.Position)]
    [InlineData(ChannelType.ColorRed, AttributeClass.Color)]
    [InlineData(ChannelType.ColorGreen, AttributeClass.Color)]
    [InlineData(ChannelType.ColorBlue, AttributeClass.Color)]
    [InlineData(ChannelType.ColorWhite, AttributeClass.Color)]
    [InlineData(ChannelType.ColorAmber, AttributeClass.Color)]
    [InlineData(ChannelType.ColorUv, AttributeClass.Color)]
    [InlineData(ChannelType.ColorWheel, AttributeClass.Color)]
    [InlineData(ChannelType.Gobo, AttributeClass.Beam)]
    [InlineData(ChannelType.GoboRotation, AttributeClass.Beam)]
    [InlineData(ChannelType.Zoom, AttributeClass.Beam)]
    [InlineData(ChannelType.Focus, AttributeClass.Beam)]
    [InlineData(ChannelType.Shutter, AttributeClass.Beam)]
    [InlineData(ChannelType.Strobe, AttributeClass.Beam)]
    [InlineData(ChannelType.Prism, AttributeClass.Beam)]
    [InlineData(ChannelType.Speed, AttributeClass.Other)]
    [InlineData(ChannelType.Macro, AttributeClass.Other)]
    [InlineData(ChannelType.ControlFunction, AttributeClass.Other)]
    [InlineData(ChannelType.Generic, AttributeClass.Other)]
    public void ToAttributeClass_MapsEveryChannelType(ChannelType channelType, AttributeClass expected)
    {
        Assert.Equal(expected, channelType.ToAttributeClass());
    }

    [Fact]
    public void ToAttributeClass_CoversEveryEnumValue()
    {
        // Guards against a future ChannelType value silently falling through to Other unnoticed.
        foreach (var value in Enum.GetValues<ChannelType>())
        {
            var result = value.ToAttributeClass();
            Assert.True(Enum.IsDefined(result));
        }
    }
}
