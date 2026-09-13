namespace DmxConsole.Core.Engine;

/// <summary>A fade-in/fade-out pair. Used at three precedence levels within a Cue: General (whole cue),
/// per-AttributeClass ("give Color 12 seconds"), and per-channel (future "Individual Times" UI) - mirrors
/// grandMA3's General Cue Times / Feature Group Timing / Individual Attribute Timing hierarchy.</summary>
public sealed record CueTiming(TimeSpan FadeInTime, TimeSpan FadeOutTime);
