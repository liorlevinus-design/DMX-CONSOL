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
}
