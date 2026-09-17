namespace DmxConsole.Core.Engine;

/// <summary>A Cue's fade timing, one flat set of values for the whole Cue - matches Compulite
/// Vector's actual Time mode (TIME-IN/TIME-OUT/DELAY-IN/DELAY-OUT, see
/// docs/VECTOR_EDITOR_TOOLBAR_REFERENCE.md §3 "Time mode"), not grandMA3's per-AttributeClass
/// Feature Group Timing this type used to model. TimeIn/TimeOut apply to channels moving to a
/// higher/lower value respectively; DelayIn/DelayOut hold the channel frozen at its starting value
/// before that channel's own fade begins.</summary>
public sealed record CueTiming(TimeSpan TimeIn, TimeSpan TimeOut, TimeSpan DelayIn, TimeSpan DelayOut)
{
    public static CueTiming Default => new(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), TimeSpan.Zero, TimeSpan.Zero);
}
