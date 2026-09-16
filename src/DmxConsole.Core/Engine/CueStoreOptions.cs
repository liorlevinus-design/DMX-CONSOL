namespace DmxConsole.Core.Engine;

/// <summary>Everything CueList.RecordCue/UpdateCue need beyond the target Cue's identity: the
/// timing to give it, its trigger mode/wait time, and which of the five STORE OPTIONS filters
/// decides what actually gets recorded.</summary>
public sealed record CueStoreOptions(CueTiming Timing, CueTriggerMode TriggerMode, TimeSpan WaitTime, CueStoreFilter Filter)
{
    public static CueStoreOptions Default => new(CueTiming.Default, CueTriggerMode.Manual, TimeSpan.Zero, CueStoreFilter.AllStage);
}
