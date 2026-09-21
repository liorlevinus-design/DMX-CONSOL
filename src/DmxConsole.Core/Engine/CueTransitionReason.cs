namespace DmxConsole.Core.Engine;

/// <summary>
/// WHY a CueList transitioned to a new current cue - AutoFollow bug slice. AutoFollow must only
/// arm when a cue is entered as part of FORWARD PLAYBACK PROGRESSION, never merely because it
/// became the current cue. This is the model StartTransitionTo uses to decide that per-transition,
/// instead of a one-off "if came from Back, skip follow" special case.
/// </summary>
public enum CueTransitionReason
{
    /// <summary>A live GO, or an AutoFollow-chained advance (Tick()'s own auto-advance calls
    /// Go() internally, so a chained follow naturally reuses this same reason - "GO into a cue"
    /// and "AutoFollow into a cue" are both forward progression, never distinguished further).
    /// The ONLY reason that arms AutoFollow.</summary>
    Go,

    /// <summary>BACK - a step backward is never "forward progression", regardless of the target
    /// cue's own TriggerMode. Never arms AutoFollow.</summary>
    Back,

    /// <summary>GO TO CUE X / any direct jump to an arbitrary cue - direct navigation, not
    /// progression. Landing here must hold, even on an AutoFollow cue, until the operator
    /// presses GO. Never arms AutoFollow.</summary>
    Jump,
}
