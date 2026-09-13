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
/// on universe 2 -> 180". Cues, Presets, Selection-scoped Clear, etc. all key off this,
/// not off raw ChannelType.
/// </summary>
public enum AttributeClass
{
    Intensity,
    Position,
    Color,
    Beam,

    /// <summary>Anything that doesn't fit the four core classes (control/macro channels, generic).</summary>
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

    /// <summary>Which operator-facing attribute group this channel type belongs to.</summary>
    public static AttributeClass ToAttributeClass(this ChannelType type) => type switch
    {
        ChannelType.Dimmer => AttributeClass.Intensity,

        ChannelType.Pan or ChannelType.PanFine or
        ChannelType.Tilt or ChannelType.TiltFine => AttributeClass.Position,

        ChannelType.ColorRed or ChannelType.ColorGreen or ChannelType.ColorBlue or
        ChannelType.ColorWhite or ChannelType.ColorAmber or ChannelType.ColorUv or
        ChannelType.ColorWheel => AttributeClass.Color,

        ChannelType.Gobo or ChannelType.GoboRotation or ChannelType.Zoom or
        ChannelType.Focus or ChannelType.Shutter or ChannelType.Strobe or
        ChannelType.Prism => AttributeClass.Beam,

        _ => AttributeClass.Other, // Speed, Macro, ControlFunction, Generic
    };
}
