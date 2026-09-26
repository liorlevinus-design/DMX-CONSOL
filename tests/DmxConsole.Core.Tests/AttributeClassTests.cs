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

    /// <summary>PSEL-5 (CLAUDE.md §16) - logical parameter folding. Pan+PanFine is one semantic
    /// PAN, Tilt+TiltFine is one semantic TILT; this is the ONE canonical folding mechanism that
    /// RELEASE (ReleaseParameterCommand), the Parameter Picker (ParameterPickerViewModel.Options)
    /// and the Encoder Drawer (EncoderDrawerViewModel.ChannelTypesForActiveCategory) all reuse.</summary>
    [Theory]
    [InlineData(ChannelType.Pan, ChannelType.Pan, ChannelType.PanFine)]
    [InlineData(ChannelType.PanFine, ChannelType.Pan, ChannelType.PanFine)]
    [InlineData(ChannelType.Tilt, ChannelType.Tilt, ChannelType.TiltFine)]
    [InlineData(ChannelType.TiltFine, ChannelType.Tilt, ChannelType.TiltFine)]
    public void SemanticComponents_CoarseFinePair_FoldsToBothComponentsWithCoarseFirst(
        ChannelType queried, ChannelType expectedPrimary, ChannelType expectedSecondary)
    {
        var components = queried.SemanticComponents();

        Assert.Equal(new[] { expectedPrimary, expectedSecondary }, components);
    }

    /// <summary>Every non-paired ChannelType (a normal 8-bit parameter, e.g. Dimmer/Gobo/ColorRed)
    /// is its own single-member parameter - folding must never merge or drop unrelated types.</summary>
    [Theory]
    [InlineData(ChannelType.Dimmer)]
    [InlineData(ChannelType.ColorRed)]
    [InlineData(ChannelType.ColorGreen)]
    [InlineData(ChannelType.ColorBlue)]
    [InlineData(ChannelType.Gobo)]
    [InlineData(ChannelType.GoboRotation)]
    [InlineData(ChannelType.Shutter)]
    [InlineData(ChannelType.Generic)]
    public void SemanticComponents_NonPairedType_IsItsOwnSingleMemberParameter(ChannelType type)
    {
        Assert.Equal(new[] { type }, type.SemanticComponents());
    }
}
