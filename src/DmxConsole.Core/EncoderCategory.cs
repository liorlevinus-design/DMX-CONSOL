namespace DmxConsole.Core;

/// <summary>
/// The bank/category grouping the fixed Encoder Drawer uses to organize parameters - grounded in
/// Compulite Vector's actual documented Editor Toolbar banks (INTENS/POS/COLOR/BEAM/IMAGE/SHAPE,
/// docs/VECTOR_EDITOR_TOOLBAR_REFERENCE.md's source manual), NOT a rename of <see cref="AttributeClass"/>.
/// This is a separate, UI-facing categorization used only by the Encoder Drawer - Presets, Cue
/// timing, and Programmer provenance all keep using AttributeClass exactly as before.
/// </summary>
public enum EncoderCategory { Intensity, Position, Color, Beam, Image, Shape }

public static class EncoderCategoryExtensions
{
    /// <summary>Maps a channel to its Encoder Drawer bank, per Vector's own documented split:
    /// POSITION "Pan, tilt, movement speed"; COLOR "Cyan, yellow, magenta, color wheels, color
    /// speed"; BEAM "Focus, zoom, iris"; IMAGE "Gobo wheels, prism, gobo spin"; SHAPE "Framing
    /// shutters, frost". Returns null for channels with no documented Vector equivalent
    /// (Macro/ControlFunction/Generic) - these stay unexposed via encoders, same as today's
    /// AttributeClass.Other handling.</summary>
    public static EncoderCategory? ToEncoderCategory(this ChannelType type) => type switch
    {
        ChannelType.Dimmer => EncoderCategory.Intensity,

        ChannelType.Pan or ChannelType.PanFine or ChannelType.Tilt or ChannelType.TiltFine
            or ChannelType.Speed => EncoderCategory.Position,

        ChannelType.ColorRed or ChannelType.ColorGreen or ChannelType.ColorBlue or ChannelType.ColorWhite
            or ChannelType.ColorAmber or ChannelType.ColorUv or ChannelType.ColorWheel => EncoderCategory.Color,

        ChannelType.Focus or ChannelType.Zoom => EncoderCategory.Beam,

        ChannelType.Gobo or ChannelType.GoboRotation or ChannelType.Prism => EncoderCategory.Image,

        ChannelType.Shutter or ChannelType.Strobe => EncoderCategory.Shape,

        _ => null, // Macro, ControlFunction, Generic
    };
}
