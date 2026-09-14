namespace DmxConsole.Core.Engine;

/// <summary>Something that can be assigned to an Executor (the handle) - see Executor.cs. CueList
/// implements this; future Effect/Preset "snap" sources can too, without Executor needing to know
/// which kind of source it's holding.</summary>
public interface IPlaybackSource : IOutputLayer
{
    /// <summary>Structured introspection snapshot - see PlaybackStatus. Time-first per
    /// UX_PHILOSOPHY §9: every timing field is Elapsed/Remaining/Total - percentage is a
    /// computed, secondary property, never the primary one.</summary>
    PlaybackStatus GetStatus();
}

/// <summary>Optional capability - a source with an internal sequence position (Go/Back/Stop).
/// Both the real Playback Actions (Application layer) and CueList's own UI go through this.</summary>
public interface ISequencedPlayback
{
    void Go();
    void Back();
    void Stop();
}

/// <summary>Optional capability - a source whose running fade can be frozen in place and later
/// continued. CueList implements this for real (see CueList.cs).</summary>
public interface IPausablePlayback
{
    bool IsPaused { get; }
    void Pause();
    void Resume();
}

/// <summary>Optional capability - a layer that wants deterministic tie-breaking against other
/// layers contributing the SAME channel at the SAME Priority, instead of unspecified iteration
/// order (a confirmed real gap: List&lt;T&gt;.Sort is not stable, so two equal-priority layers'
/// relative order is arbitrary). A layer that doesn't implement this keeps exactly today's
/// behavior - correct for Programmer/EffectsEngine, which are each the only instance of their
/// kind and never actually tie with anything.</summary>
public interface IMergeAwareLayer
{
    /// <summary>Monotonically increasing (via the shared, thread-safe RevisionClock - a
    /// Lamport-style logical counter, not a wall-clock timestamp, so ordering is exact and immune
    /// to clock resolution), incremented ONLY on a genuine semantic change to what THIS SPECIFIC
    /// channel is putting out - never merely because the same tick re-reports an unchanged or
    /// continuously-fading value, and never as a blanket claim over every channel a layer
    /// happens to touch. "Latest" means "who last gave a new instruction to this parameter",
    /// not "who was iterated last this tick" and not "who last changed anything, anywhere".</summary>
    bool TryGetRevision(int universeId, int channelIndex, out long revision);
}

/// <summary>Resolves the stable ChannelType patched at a given address - independent of any
/// specific source's current stored/tracked data. Implemented by Patch. This is what lets
/// Executor classify a channel as Intensity (for fader-scaling purposes) without depending on
/// whether the channel happens to be present in whatever a specific CueList/Cue currently
/// stores - a distinction that matters once Tracking exists (a tracked-but-unstored channel is
/// still semantically the same ChannelType it always was).</summary>
public interface IChannelTypeLookup
{
    bool TryGetChannelType(int universeId, int channelIndex, out ChannelType channelType);
}
