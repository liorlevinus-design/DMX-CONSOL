namespace DmxConsole.Core;

/// <summary>
/// Semantic role of a fixture channel. Used for blend rules (HTP vs LTP) and for
/// applying Pan/Tilt calibration in the output pipeline.
/// </summary>
public enum ChannelType
{
    Generic,
    Dimmer,
    Pan,
    PanFine,
    Tilt,
    TiltFine,
    ColorRed,
    ColorGreen,
    ColorBlue,
    ColorWhite,
    ColorAmber,
    ColorUv,
    Gobo,
    GoboRotation,
    Zoom,
    Focus,
    Shutter,
    Strobe,
    ColorWheel,
    Prism,
    Speed,
    Macro,
    ControlFunction,
}

/// <summary>
/// Blend rule applied when merging multiple layers (Programmer, cues, effects) that
/// write to the same channel at the same time.
/// </summary>
public enum BlendMode
{
    /// <summary>Highest Takes Precedence - typical for intensity/dimmer channels.</summary>
    Htp,

    /// <summary>Latest Takes Precedence - typical for position/color/gobo channels.</summary>
    Ltp,
}

/// <summary>
/// The semantic group a channel belongs to from the operator's point of view - this is
/// what lets the console be driven as "Fixture 12 -> Color -> Blue" instead of "channel 340
/// on universe 2 -> 180". Cues, Presets, Selection-scoped Clear, Release, HOME, the Encoder
/// Drawer, and Fixtures LIVE all key off this ONE six-family model (docs/COMMAND_SURFACE_KEY_SPEC.md
/// §23.1, docs/OPERATOR_UX_ROADMAP.md) - not off raw ChannelType, and not off a second parallel
/// taxonomy. This type used to hold only four values (Intensity/Position/Color/Beam) while a
/// separate `EncoderCategory` type (now removed) held six (+Image/Shape) for the Encoder Drawer
/// alone; the two have been unified here per explicit operator direction ("do not create another
/// family taxonomy").
/// </summary>
public enum AttributeClass
{
    Intensity,
    Position,
    Color,
    Beam,
    Image,
    Shape,

    /// <summary>Anything that doesn't fit the six core families (control/macro channels, generic).
    /// Never offered as a selectable family in the UI (the Encoder Drawer's own category list is
    /// the six real families above, in that fixed order) - this exists purely so ToAttributeClass
    /// stays a total, non-nullable function.</summary>
    Other,
}

public static class ChannelTypeExtensions
{
    /// <summary>Default blend mode conventionally used for each channel type.</summary>
    public static BlendMode DefaultBlendMode(this ChannelType type) => type switch
    {
        ChannelType.Dimmer => BlendMode.Htp,
        _ => BlendMode.Ltp,
    };

    public static bool IsPosition(this ChannelType type) =>
        type is ChannelType.Pan or ChannelType.PanFine or ChannelType.Tilt or ChannelType.TiltFine;

    /// <summary>Which operator-facing attribute family this channel type belongs to - the single
    /// authoritative six-family default mapping (docs/COMMAND_SURFACE_KEY_SPEC.md §23.1):
    /// Prism and Shutter/Strobe (beam effects) classify as Beam, not Image/Shape; Gobo/GoboRotation
    /// classify as Image; nothing in today's ChannelType enum represents a framing-shutter/blade/
    /// keystone mechanism, so Shape has no default member yet (the family still exists for future
    /// channel types and for explicit fixture-profile overrides). A future per-profile override
    /// field on FixtureChannel (not built yet - no such field exists today) would be the intended
    /// way to reclassify a specific fixture's Shutter as a framing device rather than a beam
    /// effect, WITHOUT introducing a second family taxonomy alongside this one.</summary>
    public static AttributeClass ToAttributeClass(this ChannelType type) => type switch
    {
        ChannelType.Dimmer => AttributeClass.Intensity,

        ChannelType.Pan or ChannelType.PanFine or ChannelType.Tilt or ChannelType.TiltFine
            or ChannelType.Speed => AttributeClass.Position,

        ChannelType.ColorRed or ChannelType.ColorGreen or ChannelType.ColorBlue or
        ChannelType.ColorWhite or ChannelType.ColorAmber or ChannelType.ColorUv or
        ChannelType.ColorWheel => AttributeClass.Color,

        ChannelType.Focus or ChannelType.Zoom or ChannelType.Prism or
        ChannelType.Shutter or ChannelType.Strobe => AttributeClass.Beam,

        ChannelType.Gobo or ChannelType.GoboRotation => AttributeClass.Image,

        _ => AttributeClass.Other, // Macro, ControlFunction, Generic
    };

    /// <summary>The six real operator-facing families, in the fixed Vector-bank display order
    /// (docs/COMMAND_SURFACE_KEY_SPEC.md §4) - excludes <see cref="AttributeClass.Other"/>, which
    /// is never offered as a selectable family anywhere in the UI.</summary>
    public static readonly IReadOnlyList<AttributeClass> SelectableFamilies = new[]
    {
        AttributeClass.Intensity, AttributeClass.Position, AttributeClass.Color,
        AttributeClass.Beam, AttributeClass.Image, AttributeClass.Shape,
    };
}
