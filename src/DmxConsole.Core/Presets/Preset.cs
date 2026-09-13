namespace DmxConsole.Core.Presets;

/// <summary>
/// A reusable, labeled look for one attribute group (Intensity/Position/Color/Beam) - stored
/// by ChannelType, not by raw DMX address, so the same Preset applies consistently across
/// different fixture types that share that channel (e.g. a Color preset recorded from an
/// RGBW par still makes sense applied to a moving head's RGB mixing channels). This is the
/// simple "Universal"-style preset every real console supports as its default recording mode;
/// per-fixture-type overrides (grandMA3's Selective/Global modes) are a later refinement.
/// </summary>
public sealed class Preset
{
    public Guid Id { get; } = Guid.NewGuid();

    public AttributeClass Class { get; init; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Stable, operator-facing number - unique within this Preset's Class, not globally (mirrors PatchedFixture.Number).</summary>
    public int Number { get; set; }

    public Dictionary<ChannelType, byte> Values { get; init; } = new();

    public override string ToString() => $"{Class} Preset {Number} - {Name}";
}
