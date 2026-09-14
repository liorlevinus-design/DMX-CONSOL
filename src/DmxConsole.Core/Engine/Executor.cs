namespace DmxConsole.Core.Engine;

public enum FlashMode { None, Add, Swap } // Swap is a real, named state - not silently dropped;
                                           // TrySetFlash refuses it explicitly (see below).

/// <summary>Shared, thread-safe, monotonically increasing logical clock (Lamport-style) used to
/// order semantic changes across every Executor/CueList without relying on wall-clock
/// timestamps - immune to clock resolution, exact total ordering.</summary>
public static class RevisionClock
{
    private static long _counter;
    public static long Next() => Interlocked.Increment(ref _counter);
}

/// <summary>
/// The "handle": a fader/button assignable to a Playback Source (today, only CueList), separate
/// from the source itself. Registered as its own IOutputLayer with DmxOutputEngine - fader-level
/// scaling, Flash, and Priority belong to the assignment (the handle), not to the source's own
/// content, exactly like grandMA3's Render-Style/Fix-Executor settings belong to the handle-to-
/// object relationship rather than the object.
/// </summary>
public sealed class Executor : IOutputLayer, IMergeAwareLayer
{
    public Guid Id { get; } = Guid.NewGuid();   // stable identity - never changes, survives renumber/rename
    public int Number { get; set; }             // operator-facing, renumberable later without touching Id
    public string Name { get; set; } = "";      // editable display label - never used as identity
    public int Priority { get; set; } = 200;
    public IPlaybackSource? Source { get; private set; }

    private readonly IChannelTypeLookup? _channelTypeLookup;
    private double _faderLevel = 1.0;
    private FlashMode _flash = FlashMode.None;

    /// <summary>Handle-level revisions, split by WHICH channels the change actually affects.
    /// FaderLevel/Flash mathematically only ever affect (a) Intensity-class channels' numeric
    /// scaling (every level/flash change re-scales them, by definition of TryGetChannelValue
    /// below) and (b) non-Intensity channels' on/off CONTRIBUTION GATE (EffectiveLevel &lt;= 0
    /// vs &gt; 0) - never their raw value otherwise. Two counters, each bumped only when ITS
    /// category of channel is actually affected - never a blanket "everything just changed".</summary>
    private long _intensityHandleRevision;
    private long _nonIntensityHandleRevision;

    public Executor(int number, IChannelTypeLookup? channelTypeLookup = null)
    {
        Number = number;
        _channelTypeLookup = channelTypeLookup;
    }

    string IOutputLayer.Name => $"Executor {Number}" + (Name.Length > 0 ? $" ({Name})" : "");
    public bool IsActive => Source?.IsActive == true;
    public FlashMode Flash => _flash;
    private double EffectiveLevel => _flash == FlashMode.Add ? 1.0 : _faderLevel;

    public double FaderLevel
    {
        get => _faderLevel;
        set
        {
            if (value == _faderLevel) return; // no actual change - never a meaningless bump
            bool gateWasOpen = EffectiveLevel > 0;
            _faderLevel = value;
            _intensityHandleRevision = RevisionClock.Next(); // level always re-scales Intensity's output
            if ((EffectiveLevel > 0) != gateWasOpen) _nonIntensityHandleRevision = RevisionClock.Next(); // only if the on/off gate actually flipped
        }
    }

    /// <summary>Reassignable at runtime without recreating the Executor - Source and Handle
    /// stay separate entities, exactly as required.</summary>
    public void Assign(IPlaybackSource? source)
    {
        Source = source;
        // A reassignment is a wholesale change to what this handle puts out - affects everything,
        // unlike a level/flash tweak which only affects specific classes (see above).
        _intensityHandleRevision = RevisionClock.Next();
        _nonIntensityHandleRevision = RevisionClock.Next();
    }

    // Go/Back/Stop do NOT bump either handle revision - a Go's semantic "this channel just got a
    // new instruction" is per-CHANNEL and belongs entirely to the SOURCE (CueList implements
    // IMergeAwareLayer too), not a blanket claim by the handle. This is exactly what keeps a Go
    // that only changes Color from stealing ownership of an unrelated, unchanged/tracked
    // Intensity channel.
    public void Go() { if (Source is ISequencedPlayback s) s.Go(); }
    public void Back() { if (Source is ISequencedPlayback s) s.Back(); }
    public void Stop() { if (Source is ISequencedPlayback s) s.Stop(); }
    public void Pause() { if (Source is IPausablePlayback p) p.Pause(); }
    public void Resume() { if (Source is IPausablePlayback p) p.Resume(); }

    /// <summary>Returns false (never throws, never silently no-ops) for FlashMode.Swap - Swap
    /// needs ExecutorBank-level coordination explicitly out of scope this step.</summary>
    public bool TrySetFlash(FlashMode mode)
    {
        if (mode == FlashMode.Swap) return false;
        if (mode == _flash) return true; // no actual change
        bool gateWasOpen = EffectiveLevel > 0;
        _flash = mode;
        _intensityHandleRevision = RevisionClock.Next(); // Flash always overrides Intensity's effective level
        if ((EffectiveLevel > 0) != gateWasOpen) _nonIntensityHandleRevision = RevisionClock.Next();
        return true;
    }

    /// <summary>Per-channel: the higher of (a) whichever handle-level revision applies to THIS
    /// channel's class (Intensity vs non-Intensity) and (b) the assigned Source's own per-channel
    /// revision for this specific channel, if the Source is itself IMergeAwareLayer (CueList is).
    /// This is what makes "Go changing only Color" leave an unrelated Intensity channel's
    /// revision untouched, AND what makes a FaderLevel tweak that doesn't cross the on/off gate
    /// leave non-Intensity channels' revision untouched.</summary>
    public bool TryGetRevision(int universeId, int channelIndex, out long revision)
    {
        long sourceRevision = (Source as IMergeAwareLayer)?.TryGetRevision(universeId, channelIndex, out var r) == true ? r : 0;
        bool isIntensity = _channelTypeLookup?.TryGetChannelType(universeId, channelIndex, out var type) == true
            && type.ToAttributeClass() == AttributeClass.Intensity;
        long handleRevision = isIntensity ? _intensityHandleRevision : _nonIntensityHandleRevision;
        revision = Math.Max(handleRevision, sourceRevision);
        return true;
    }

    public bool TryGetChannelValue(int universeId, int channelIndex, out byte value)
    {
        value = 0;
        if (Source is null || !Source.TryGetChannelValue(universeId, channelIndex, out var raw)) return false;

        double level = EffectiveLevel;
        bool isIntensity = _channelTypeLookup?.TryGetChannelType(universeId, channelIndex, out var type) == true
            && type.ToAttributeClass() == AttributeClass.Intensity;

        if (isIntensity) { value = (byte)Math.Round(raw * level); return true; }

        // Non-Intensity: on/off by fader level, never interpolated - proportionally scaling a
        // raw Pan/Color byte has no meaningful operator interpretation. Confirmed against real
        // consoles: MagicQ's manual states plainly that "LTP channels are not affected by the
        // master faders".
        if (level <= 0) return false;
        value = raw;
        return true;
    }

    public PlaybackStatus? GetStatus() => Source?.GetStatus();
}
