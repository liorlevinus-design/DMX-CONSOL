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

    public string Name => "Programmer";

    /// <summary>Always wins - nothing should out-rank the operator's own hands.</summary>
    public int Priority => int.MaxValue;

    public bool IsActive => !_values.IsEmpty;

    public void SetChannel(int universeId, int channelIndex, byte value) =>
        _values[(universeId, channelIndex)] = value;

    public void ClearChannel(int universeId, int channelIndex) =>
        _values.TryRemove((universeId, channelIndex), out _);

    /// <summary>Releases all live overrides (typical "Clear" console button).</summary>
    public void ClearAll() => _values.Clear();

    public bool TryGetChannelValue(int universeId, int channelIndex, out byte value) =>
        _values.TryGetValue((universeId, channelIndex), out value);

    public IReadOnlyDictionary<(int Universe, int Channel), byte> Snapshot() =>
        new Dictionary<(int, int), byte>(_values);
}
