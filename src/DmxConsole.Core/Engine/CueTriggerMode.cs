namespace DmxConsole.Core.Engine;

/// <summary>
/// AutoFollow correction slice: Trigger belongs to the TARGET/NEXT cue and describes how playback
/// enters THAT cue once the PREVIOUS cue's own transition has fully completed - never a property
/// of "what the currently-playing cue does to itself". See CueList.Tick()'s own doc comment for
/// the full state machine this drives.
/// </summary>
public enum CueTriggerMode
{
    /// <summary>Previous cue completes -> hold. Operator must press GO to enter this cue.</summary>
    Manual,

    /// <summary>Previous cue completes FULLY (its own fade, not this cue's) -> this cue starts
    /// immediately. WaitTime is irrelevant/ignored for AutoFollow - there is no delay concept
    /// here, ever, regardless of whatever value WaitTime happens to hold.</summary>
    AutoFollow,

    /// <summary>Previous cue completes FULLY -> a separate, pause-aware wait timer starts (never
    /// concurrent with the previous cue's own fade) -> once WaitTime elapses, this cue starts
    /// automatically. Requires an explicit WaitTime; a Wait cue with WaitTime == TimeSpan.Zero is
    /// not an error, but it behaves identically to AutoFollow (fires on the very next tick after
    /// completion) - an honest consequence of the model, not a special case.</summary>
    Wait,
}
