namespace DmxConsole.Core.Engine;

/// <summary>Introspection snapshot returned by IPlaybackSource.GetStatus() - a small polymorphic
/// type since different source kinds carry different data (a discriminated union via
/// inheritance, not one flat struct faking fields for kinds that don't have them).</summary>
public abstract record PlaybackStatus;

/// <summary>Elapsed/Remaining/Total - in that reading order, matching UX_PHILOSOPHY §9's "the
/// operator's real question is seconds remaining, not percent complete." PercentComplete exists
/// only as a computed convenience for a secondary/optional UI element.</summary>
public readonly record struct TimingProgress(TimeSpan Elapsed, TimeSpan Remaining, TimeSpan Total)
{
    public double PercentComplete => Total.TotalSeconds <= 0
        ? 1.0
        : Math.Clamp(Elapsed.TotalSeconds / Total.TotalSeconds, 0.0, 1.0);
}

/// <summary>FadeIn/FadeOut replace the old per-AttributeClass breakdown (H1.6 Slice 2): timing is
/// now a single flat set of values per Cue (Vector's Time mode, not grandMA3's Feature Group
/// Timing), so the only real distinction left to report is direction - up (In) vs down (Out),
/// each including its own Delay.</summary>
public sealed record CueListPlaybackStatus(
    Cue? CurrentCue,
    Cue? PendingCue, // always null in this step - no "armed but not GO'd" concept yet, reserved
    bool IsRunning,
    bool IsPaused,
    TimingProgress Overall,
    TimingProgress FadeIn,
    TimingProgress FadeOut
) : PlaybackStatus;
