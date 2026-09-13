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
}
