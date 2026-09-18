namespace DmxConsole.Application;

/// <summary>
/// Opt-in capability for an <see cref="IConsoleCommand"/> that can construct an independent,
/// freshly-constructed copy of itself - never sharing this instance's own mutable per-execution
/// Undo snapshot state (docs/COMMAND_SURFACE_KEY_SPEC.md MACROS follow-up "REPLAY INSTANCE
/// SAFETY"). A command's Undo relies on state captured during ITS OWN prior Execute() call (e.g.
/// <c>ProgrammerChannelCommandBase</c>'s <c>_previous</c> snapshot, <c>SelectionCommandBase</c>'s
/// <c>_previousSelection</c>) - the SAME instance must never be dispatched (and therefore never
/// enter the Undo stack) more than once, or two Undo-stack entries would end up sharing one
/// mutable snapshot field and silently corrupt each other's Undo.
///
/// Any caller that needs to logically "do this operation again" - MacroPlaybackService is the
/// first and, as of this writing, only one - must go through this to get a brand-new instance
/// for every dispatch, never re-dispatch a command instance that was already Executed once.
///
/// A command that does NOT implement this is, by construction, not macro-recordable
/// (see MacroRecorder) - excluded and reported, never stored/replayed unsafely.
/// </summary>
public interface IReplayableCommand
{
    /// <summary>Builds a brand-new <see cref="IConsoleCommand"/> instance with the exact same
    /// construction-time targets/parameters as this one - unexecuted, with no Undo snapshot
    /// state of its own yet.</summary>
    IConsoleCommand CreateFreshInstance();
}
