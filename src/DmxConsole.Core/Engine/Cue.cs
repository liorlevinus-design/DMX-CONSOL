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

    /// <summary>One flat set of fade timing for the whole Cue - Vector's actual Time mode
    /// (TIME-IN/TIME-OUT/DELAY-IN/DELAY-OUT), see CueTiming's own doc comment. No per-AttributeClass
    /// or per-channel override anymore (H1.6 Slice 2 - a deliberate replacement, not an addition).</summary>
    public CueTiming Timing { get; set; } = CueTiming.Default;

    /// <summary>AutoFollow correction slice: describes how THIS cue is entered once the PREVIOUS
    /// cue in the list has fully completed its own transition - Manual (hold for GO), AutoFollow
    /// (enter immediately), or Wait (enter after WaitTime, measured from the previous cue's
    /// completion). Never consulted about this cue's own current playback - see CueList.Tick's own
    /// doc comment for exactly when/how a cue's Trigger is read.</summary>
    public CueTriggerMode TriggerMode { get; set; } = CueTriggerMode.Manual;

    /// <summary>Only consulted when THIS cue's own TriggerMode is Wait (ignored for AutoFollow,
    /// meaningless for Manual) - how long after the PREVIOUS cue's transition fully completes
    /// before this cue starts automatically. Never overlaps the previous cue's own fade.</summary>
    public TimeSpan WaitTime { get; set; } = TimeSpan.Zero;

    /// <summary>The channels this Cue stores: every patched (universe, channel) -> either an absolute
    /// recorded byte or a live PresetRef, resolved at playback time (never baked in at record time).</summary>
    public IReadOnlyDictionary<(int Universe, int Channel), CueValue> Levels { get; init; }
        = new Dictionary<(int, int), CueValue>();

    public override string ToString() => $"Cue {Number} - {Name}";
}
