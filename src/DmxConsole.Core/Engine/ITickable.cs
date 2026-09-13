namespace DmxConsole.Core.Engine;

/// <summary>
/// Implemented by output layers that need to advance their own state once per engine
/// tick (effects, anything time-driven) before the merge pass reads their contributions.
/// Layers that don't need this (Programmer, CueList - they compute on demand from
/// wall-clock time already) simply don't implement it.
/// </summary>
public interface ITickable
{
    /// <summary>Called once per engine tick, before channels are read, with time elapsed since the engine started.</summary>
    void Tick(TimeSpan elapsed);
}
