using System.Collections.Concurrent;

namespace DmxConsole.Core.Engine;

/// <summary>
/// The "live edit" layer: values the operator is setting right now via faders/encoders,
/// outside of any cue. Always the highest-priority layer - it must be able to override
/// anything a cue or effect is doing, which is how a console lets you grab a fixture live.
/// </summary>
public sealed class Programmer : IOutputLayer
{
    private readonly ConcurrentDictionary<(int Universe, int Channel), byte> _values = new();

    /// <summary>
    /// Channels temporarily suppressed from output without discarding their stored value
    /// (Knockout). Value is unused - this is just a concurrent set. A knocked-out channel
    /// still shows up in <see cref="HasStoredValue"/> but never in <see cref="TryGetChannelValue"/>,
    /// so the merge engine treats it as "not contributing" while Restore can bring it back exactly.
    /// </summary>
    private readonly ConcurrentDictionary<(int Universe, int Channel), byte> _knockedOut = new();

    /// <summary>Parameter TIME slice (CLAUDE.md §16/ROADMAP §9a): optional per-channel fade timing
    /// overrides, parallel to <see cref="_values"/> but on an INDEPENDENT axis - setting/clearing a
    /// channel's VALUE never touches its timing and vice versa (each is its own Undo concern; see
    /// SetParameterTimingCommand vs SetParameterValuesCommand). A channel may have a timing entry
    /// with no value entry (TIME typed before AT) or a value with no timing (the common case -
    /// falls back to the Cue's own flat CueTiming at playback). Either tuple side may be null,
    /// meaning "no override for that side" - never removed from the dictionary just because one
    /// side is null, only when BOTH sides become null (see <see cref="SetTimingRaw"/>).</summary>
    private readonly ConcurrentDictionary<(int Universe, int Channel), (TimeSpan? TimeIn, TimeSpan? TimeOut)> _timing = new();

    public string Name => "Programmer";

    /// <summary>Always wins - nothing should out-rank the operator's own hands.</summary>
    public int Priority => int.MaxValue;

    public bool IsActive => !_values.IsEmpty || !_knockedOut.IsEmpty;

    public void SetChannel(int universeId, int channelIndex, byte value) =>
        _values[(universeId, channelIndex)] = value;

    /// <summary>Fully releases a channel: discards both its stored value and any knockout state.</summary>
    public void ClearChannel(int universeId, int channelIndex)
    {
        _values.TryRemove((universeId, channelIndex), out _);
        _knockedOut.TryRemove((universeId, channelIndex), out _);
    }

    /// <summary>Releases all live overrides (typical "Clear" console button) - including every
    /// per-channel timing override (Parameter TIME slice). Deliberately NOT mirrored onto
    /// <see cref="ClearChannel"/>: that method backs per-channel Undo restore in
    /// ProgrammerChannelCommandBase (VALUE-only commands), and touching timing there would let an
    /// unrelated AT's Undo wipe out an independently-set TIME override on the same channel -
    /// exactly the cross-axis contamination Parameter TIME's independence requirement forbids.</summary>
    public void ClearAll()
    {
        _values.Clear();
        _knockedOut.Clear();
        _timing.Clear();
    }

    /// <summary>Suppresses a channel's contribution to output without discarding its stored value.</summary>
    public void Knockout(int universeId, int channelIndex) =>
        _knockedOut[(universeId, channelIndex)] = 0;

    /// <summary>Un-suppresses a previously knocked-out channel - its stored value contributes again.</summary>
    public void Restore(int universeId, int channelIndex) =>
        _knockedOut.TryRemove((universeId, channelIndex), out _);

    public bool IsKnockedOut(int universeId, int channelIndex) =>
        _knockedOut.ContainsKey((universeId, channelIndex));

    /// <summary>
    /// Engine-facing query used by the merge loop: returns false while the channel is
    /// knocked out, even though its value is still remembered internally.
    /// </summary>
    public bool TryGetChannelValue(int universeId, int channelIndex, out byte value)
    {
        var key = (universeId, channelIndex);
        if (_knockedOut.ContainsKey(key))
        {
            value = 0;
            return false;
        }

        return _values.TryGetValue(key, out value);
    }

    /// <summary>
    /// Admin-facing query that ignores knockout state - used by Commands that need to know
    /// "what value is actually stored here" (e.g. to decide what Release should undo to).
    /// </summary>
    public bool HasStoredValue(int universeId, int channelIndex, out byte value) =>
        _values.TryGetValue((universeId, channelIndex), out value);

    public IReadOnlyDictionary<(int Universe, int Channel), byte> Snapshot() =>
        new Dictionary<(int, int), byte>(_values);

    /// <summary>Sets this channel's TimeIn override, preserving whatever TimeOut override (if any)
    /// already exists on it - "TIME IN 5" must never clobber a previously-set TIME OUT on the same
    /// target, and vice versa (each side is written independently).</summary>
    public void SetTimeIn(int universeId, int channelIndex, TimeSpan value)
    {
        var key = (universeId, channelIndex);
        _timing.AddOrUpdate(key, (value, (TimeSpan?)null), (_, existing) => (value, existing.TimeOut));
    }

    /// <summary>Sets this channel's TimeOut override, preserving any existing TimeIn override.</summary>
    public void SetTimeOut(int universeId, int channelIndex, TimeSpan value)
    {
        var key = (universeId, channelIndex);
        _timing.AddOrUpdate(key, ((TimeSpan?)null, value), (_, existing) => (existing.TimeIn, value));
    }

    /// <summary>True if this channel has ANY timing override (TimeIn and/or TimeOut) recorded -
    /// used both by RecordCue (to decide whether a CueValue needs its override fields populated)
    /// and by SetParameterTimingCommand's own Undo snapshot.</summary>
    public bool TryGetTiming(int universeId, int channelIndex, out TimeSpan? timeIn, out TimeSpan? timeOut)
    {
        if (_timing.TryGetValue((universeId, channelIndex), out var t))
        {
            timeIn = t.TimeIn;
            timeOut = t.TimeOut;
            return t.TimeIn is not null || t.TimeOut is not null;
        }

        timeIn = null;
        timeOut = null;
        return false;
    }

    /// <summary>Undo-restore primitive: sets both timing sides to exactly the given (possibly null)
    /// values, removing the dictionary entry entirely once both sides are null - never leaves a
    /// dangling (null, null) entry behind after an Undo restores a channel to "no override".</summary>
    public void SetTimingRaw(int universeId, int channelIndex, TimeSpan? timeIn, TimeSpan? timeOut)
    {
        var key = (universeId, channelIndex);
        if (timeIn is null && timeOut is null) _timing.TryRemove(key, out _);
        else _timing[key] = (timeIn, timeOut);
    }
}
