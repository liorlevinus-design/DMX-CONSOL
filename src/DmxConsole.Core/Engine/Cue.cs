namespace DmxConsole.Core.Engine;

/// <summary>
/// One recorded look: the channels this Cue actually stores/owns - each either an absolute byte or a
/// live reference to a Preset - plus timing. `Levels` is deliberately sparse: nothing in this type forces
/// every patched channel to be present. Today's <see cref="CueList.RecordCue"/> happens to always snapshot
/// every channel (a recording *policy*, not a data-model requirement) - a future sparse/"Cue Only" record
/// mode, and full Tracking (inheriting values from a previous Cue when absent here), can be layered on top
/// of this same type without changing it.
/// </summary>
public sealed class Cue
{
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Display/ordering number - supports fractional numbers (e.g. 1.5) like a real console.</summary>
    public double Number { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Whole-cue fallback timing (grandMA3's "General Cue Times" / MagicQ's "General Times").
    /// Used for any channel that has no more specific override below.</summary>
    public CueTiming GeneralTiming { get; set; } = new(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));

    /// <summary>Per-AttributeClass timing overrides (grandMA3's "Feature Group Timing" / MagicQ's per-type
    /// General Times, e.g. "give Color 12 seconds"). Overrides <see cref="GeneralTiming"/> for channels of
    /// that class; sparse by default - only classes explicitly given a different time are present.</summary>
    public IReadOnlyDictionary<AttributeClass, CueTiming> AttributeTiming { get; init; }
        = new Dictionary<AttributeClass, CueTiming>();

    /// <summary>Per-channel timing overrides (grandMA3's "Individual Attribute Timing" / MagicQ's
    /// "Individual/Split Times"). Not written to by any UI yet - exists so a future per-head/per-attribute
    /// timing feature does not require a data-model rewrite. Overrides <see cref="AttributeTiming"/>.</summary>
    public IReadOnlyDictionary<(int Universe, int Channel), CueTiming> ChannelTiming { get; init; }
        = new Dictionary<(int, int), CueTiming>();

    /// <summary>The channels this Cue stores: every patched (universe, channel) -> either an absolute
    /// recorded byte or a live PresetRef, resolved at playback time (never baked in at record time).</summary>
    public IReadOnlyDictionary<(int Universe, int Channel), CueValue> Levels { get; init; }
        = new Dictionary<(int, int), CueValue>();

    /// <summary>Resolves the fade timing to use for one stored channel, applying the
    /// ChannelTiming &gt; AttributeTiming &gt; GeneralTiming precedence.</summary>
    public CueTiming TimingFor((int Universe, int Channel) key, CueValue value)
    {
        if (ChannelTiming.TryGetValue(key, out var channelTiming)) return channelTiming;
        if (AttributeTiming.TryGetValue(value.ChannelType.ToAttributeClass(), out var attributeTiming)) return attributeTiming;
        return GeneralTiming;
    }

    public override string ToString() => $"Cue {Number} - {Name}";
}
