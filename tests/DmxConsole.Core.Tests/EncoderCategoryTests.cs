using Xunit;

namespace DmxConsole.Core.Tests;

/// <summary>Work-plan Milestone 1 (2026-09-16) - EncoderCategory mirrors Vector's own documented
/// Editor Toolbar banks (INTENS/POS/COLOR/BEAM/IMAGE/SHAPE), a separate categorization from
/// AttributeClass used only by the Encoder Drawer.</summary>
public class EncoderCategoryTests
{
    [Theory]
    [InlineData(ChannelType.Dimmer, EncoderCategory.Intensity)]
    [InlineData(ChannelType.Pan, EncoderCategory.Position)]
    [InlineData(ChannelType.PanFine, EncoderCategory.Position)]
    [InlineData(ChannelType.Tilt, EncoderCategory.Position)]
    [InlineData(ChannelType.TiltFine, EncoderCategory.Position)]
    [InlineData(ChannelType.Speed, EncoderCategory.Position)] // Vector: "Pan, tilt, movement speed"
    [InlineData(ChannelType.ColorRed, EncoderCategory.Color)]
    [InlineData(ChannelType.ColorGreen, EncoderCategory.Color)]
    [InlineData(ChannelType.ColorBlue, EncoderCategory.Color)]
    [InlineData(ChannelType.ColorWhite, EncoderCategory.Color)]
    [InlineData(ChannelType.ColorAmber, EncoderCategory.Color)]
    [InlineData(ChannelType.ColorUv, EncoderCategory.Color)]
    [InlineData(ChannelType.ColorWheel, EncoderCategory.Color)]
    [InlineData(ChannelType.Focus, EncoderCategory.Beam)]
    [InlineData(ChannelType.Zoom, EncoderCategory.Beam)]
    [InlineData(ChannelType.Gobo, EncoderCategory.Image)]
    [InlineData(ChannelType.GoboRotation, EncoderCategory.Image)]
    [InlineData(ChannelType.Prism, EncoderCategory.Image)]
    [InlineData(ChannelType.Shutter, EncoderCategory.Shape)]
    [InlineData(ChannelType.Strobe, EncoderCategory.Shape)]
    public void ToEncoderCategory_MapsPerVectorsDocumentedBanks(ChannelType channelType, EncoderCategory expected)
    {
        Assert.Equal(expected, channelType.ToEncoderCategory());
    }

    [Theory]
    [InlineData(ChannelType.Macro)]
    [InlineData(ChannelType.ControlFunction)]
    [InlineData(ChannelType.Generic)]
    public void ToEncoderCategory_ReturnsNull_ForChannelsWithNoVectorEquivalent(ChannelType channelType)
    {
        Assert.Null(channelType.ToEncoderCategory());
    }
}
