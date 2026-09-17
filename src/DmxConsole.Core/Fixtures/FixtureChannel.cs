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

    private string _unit = "DMX";

    /// <summary>Display unit for this channel's calibrated physical range - "%", "°", or the
    /// default "DMX" (meaning no calibration: MinValue/MaxValue are the raw 0-255 byte range
    /// itself, so DisplayValue below is an identity passthrough). Never guess a physical unit a
    /// profile hasn't actually declared - "DMX" is the honest default, not a placeholder to fill
    /// in with a fabricated range. This is semantic metadata the fixture profile itself declares
    /// (see GenericFixtureLibrary) - nothing infers a unit from ChannelType; a channel with no
    /// declared calibration always falls back to raw DMX, never a guessed range.</summary>
    public string Unit
    {
        get => _unit;
        init => _unit = value ?? throw new ArgumentNullException(nameof(value), "FixtureChannel.Unit cannot be null - use the default \"DMX\" for an uncalibrated channel.");
    }

    private double _minValue;

    /// <summary>The physical value at raw byte 0. Not required to be numerically less than
    /// MaxValue - a channel calibrated with a reversed physical range (e.g. a Pan/Tilt profile
    /// where byte 0 is the fixture's maximum angle) is fully supported; ToDisplayValue/
    /// FromDisplayValue preserve whatever direction the profile declares, they never assume
    /// ascending.</summary>
    public double MinValue
    {
        get => _minValue;
        init => _minValue = double.IsFinite(value) ? value : throw new ArgumentException("FixtureChannel.MinValue must be a finite number (not NaN or Infinity).", nameof(value));
    }

    private double _maxValue = 255;

    /// <summary>The physical value at raw byte 255. See MinValue's own doc comment re: reversed
    /// ranges.</summary>
    public double MaxValue
    {
        get => _maxValue;
        init => _maxValue = double.IsFinite(value) ? value : throw new ArgumentException("FixtureChannel.MaxValue must be a finite number (not NaN or Infinity).", nameof(value));
    }

    /// <summary>Converts a raw byte to this channel's calibrated display value. Identity for the
    /// default Unit="DMX"/Min=0/Max=255 - zero behavior change for any channel that hasn't
    /// declared real calibration. Direction-agnostic: works identically whether MinValue &lt;
    /// MaxValue (ascending) or MinValue &gt; MaxValue (reversed) - it's a plain linear
    /// interpolation between the two declared endpoints, never an assumption about which is
    /// numerically larger. MinValue == MaxValue (a zero-width range) is a deliberately supported,
    /// explicit case: every raw byte maps to that single fixed display value.</summary>
    public double ToDisplayValue(byte raw) => MinValue + (raw / 255.0) * (MaxValue - MinValue);

    /// <summary>Converts a display-unit value back to a raw byte - the numeric-entry validation
    /// path. Clamps the input to [min(MinValue,MaxValue), max(MinValue,MaxValue)] first (so a
    /// reversed range still clamps to the correct physical bounds, not to MinValue..MaxValue
    /// taken literally in declaration order), then to [0,255] after rounding. A non-finite input
    /// (NaN/Infinity - e.g. from an unvalidated numeric-entry field) is rejected with an
    /// ArgumentException rather than silently producing an undefined byte; callers that accept
    /// free-form text (EncoderDrawerViewModel.TrySetDisplayValue) check double.IsFinite first and
    /// never let it reach here. MinValue == MaxValue always returns raw byte 0 - there is no
    /// meaningful inverse for a zero-width range, so this is the one deliberate, documented
    /// exception to "the byte you get back reflects the display value you typed".</summary>
    public byte FromDisplayValue(double display)
    {
        if (!double.IsFinite(display))
            throw new ArgumentException("Display value must be a finite number (not NaN or Infinity).", nameof(display));

        if (MaxValue == MinValue) return 0;

        double clampedDisplay = Math.Clamp(display, Math.Min(MinValue, MaxValue), Math.Max(MinValue, MaxValue));
        double raw = (clampedDisplay - MinValue) / (MaxValue - MinValue) * 255.0;
        return (byte)Math.Clamp(Math.Round(raw), 0, 255);
    }
}
