namespace DmxConsole.Core.Engine;

/// <summary>
/// A contributor to the final DMX output: the live Programmer, a running cue, a running
/// effect, etc. The merge engine asks every layer, in priority order, whether it wants
/// to contribute a value for a given channel this tick.
/// </summary>
public interface IOutputLayer
{
    string Name { get; }

    /// <summary>Higher priority is considered "more live" and wins on LTP channels.</summary>
    int Priority { get; }

    /// <summary>Whether this layer currently contributes anything at all (skip cheaply if not).</summary>
    bool IsActive { get; }

    /// <summary>Attempts to get this layer's contribution for one channel. Returns false if it doesn't touch that channel.</summary>
    bool TryGetChannelValue(int universeId, int channelIndex, out byte value);
}
