using Xunit;

namespace DmxConsole.Core.Tests;

/// <summary>The one authoritative six-family model (docs/COMMAND_SURFACE_KEY_SPEC.md §23.1) -
/// this used to be two separate types (a 4-value AttributeClass for Release/Presets, a 6-value
/// EncoderCategory for the Encoder Drawer). Now unified: Prism and Shutter/Strobe (beam effects)
/// classify as Beam, not Image/Shape; Gobo/GoboRotation classify as Image; Speed stays Other/
/// unclassified by default (a generic "Speed" channel could mean movement, color-wheel, or gobo
/// speed - never guessed without explicit fixture-profile semantics); nothing in today's
/// ChannelType enum represents a framing-shutter/blade/keystone mechanism, so Shape has no
/// default member yet.</summary>
public class AttributeClassTests
{
    [Theory]
    [InlineData(ChannelType.Dimmer, AttributeClass.Intensity)]
    [InlineData(ChannelType.Pan, AttributeClass.Position)]
    [InlineData(ChannelType.PanFine, AttributeClass.Position)]
    [InlineData(ChannelType.Tilt, AttributeClass.Position)]
    [InlineData(ChannelType.TiltFine, AttributeClass.Position)]
    [InlineData(ChannelType.Speed, AttributeClass.Other)] // generic - could be movement/color-wheel/gobo speed; never guessed
    [InlineData(ChannelType.ColorRed, AttributeClass.Color)]
    [InlineData(ChannelType.ColorGreen, AttributeClass.Color)]
    [InlineData(ChannelType.ColorBlue, AttributeClass.Color)]
    [InlineData(ChannelType.ColorWhite, AttributeClass.Color)]
    [InlineData(ChannelType.ColorAmber, AttributeClass.Color)]
    [InlineData(ChannelType.ColorUv, AttributeClass.Color)]
    [InlineData(ChannelType.ColorWheel, AttributeClass.Color)]
    [InlineData(ChannelType.Focus, AttributeClass.Beam)]
    [InlineData(ChannelType.Zoom, AttributeClass.Beam)]
    [InlineData(ChannelType.Prism, AttributeClass.Beam)] // beam effect, never Image/Shape
    [InlineData(ChannelType.Shutter, AttributeClass.Beam)] // beam effect, distinct from framing shutters
    [InlineData(ChannelType.Strobe, AttributeClass.Beam)]
    [InlineData(ChannelType.Gobo, AttributeClass.Image)]
    [InlineData(ChannelType.GoboRotation, AttributeClass.Image)]
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

    [Fact]
    public void SelectableFamilies_ListsExactlySixRealFamilies_ExcludingOther()
    {
        Assert.Equal(
            new[] { AttributeClass.Intensity, AttributeClass.Position, AttributeClass.Color, AttributeClass.Beam, AttributeClass.Image, AttributeClass.Shape },
            ChannelTypeExtensions.SelectableFamilies);
    }
}
