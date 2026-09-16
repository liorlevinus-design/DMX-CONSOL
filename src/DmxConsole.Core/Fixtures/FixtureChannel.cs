namespace DmxConsole.Core.Fixtures;

/// <summary>
/// One channel within a fixture mode's DMX footprint.
/// </summary>
public sealed class FixtureChannel
{
    /// <summary>Human-readable label, e.g. "Pan", "Red", "Shutter/Strobe".</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Semantic role, used for blend rules and calibration.</summary>
    public ChannelType Type { get; init; } = ChannelType.Generic;

    /// <summary>Zero-based offset from the fixture's start address.</summary>
    public int Offset { get; init; }

    /// <summary>Value applied when the fixture is patched and has never been touched.</summary>
    public byte DefaultValue { get; init; }

    /// <summary>Explicit blend override; if null, ChannelType.DefaultBlendMode() is used.</summary>
    public BlendMode? BlendModeOverride { get; init; }

    public BlendMode EffectiveBlendMode => BlendModeOverride ?? Type.DefaultBlendMode();

    /// <summary>Display unit for this channel's calibrated physical range - "%", "°", or the
    /// default "DMX" (meaning no calibration: MinValue/MaxValue are the raw 0-255 byte range
    /// itself, so DisplayValue below is an identity passthrough). Never guess a physical unit a
    /// profile hasn't actually declared - "DMX" is the honest default, not a placeholder to fill
    /// in with a fabricated range.</summary>
    public string Unit { get; init; } = "DMX";

    /// <summary>The physical value at raw byte 0.</summary>
    public double MinValue { get; init; } = 0;

    /// <summary>The physical value at raw byte 255.</summary>
    public double MaxValue { get; init; } = 255;

    /// <summary>Converts a raw byte to this channel's calibrated display value. Identity for the
    /// default Unit="DMX"/Min=0/Max=255 - zero behavior change for any channel that hasn't
    /// declared real calibration.</summary>
    public double ToDisplayValue(byte raw) => MinValue + (raw / 255.0) * (MaxValue - MinValue);

    /// <summary>Converts a display-unit value back to a raw byte, clamping to [MinValue, MaxValue]
    /// before conversion and to [0,255] after - the numeric-entry validation path.</summary>
    public byte FromDisplayValue(double display)
    {
        double clampedDisplay = Math.Clamp(display, Math.Min(MinValue, MaxValue), Math.Max(MinValue, MaxValue));
        double raw = MaxValue == MinValue ? 0 : (clampedDisplay - MinValue) / (MaxValue - MinValue) * 255.0;
        return (byte)Math.Clamp(Math.Round(raw), 0, 255);
    }
}
