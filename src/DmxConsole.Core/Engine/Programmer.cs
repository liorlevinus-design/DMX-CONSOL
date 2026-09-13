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

    /// <summary>Releases all live overrides (typical "Clear" console button).</summary>
    public void ClearAll()
    {
        _values.Clear();
        _knockedOut.Clear();
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
}
