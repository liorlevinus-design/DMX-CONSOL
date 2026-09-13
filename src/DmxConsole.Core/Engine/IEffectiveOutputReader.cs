namespace DmxConsole.Core.Engine;

/// <summary>
/// Read-only view of "what the engine's most recent tick actually computed" for one
/// channel - the fully merged result of every output layer (Cues, Effects, Programmer,
/// defaults), not any single layer's own contribution. This is what a Relative adjustment
/// ("raise Fronts by 20") should treat as the current value: a Cue could be driving the
/// fixture with no Programmer override present at all, and the operator's "+20" should
/// still land relative to what they actually see, not relative to an empty Programmer slot.
///
/// Returns 0 for a universe the engine has never ticked (e.g. before Start() is first
/// called) - there genuinely is no "current effective output" yet in that state.
/// </summary>
public interface IEffectiveOutputReader
{
    byte GetEffectiveValue(int universeId, int channelIndex);
}
