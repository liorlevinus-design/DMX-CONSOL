namespace DmxConsole.Application.Macros;

/// <summary>Which kind of operation a <see cref="MacroStep"/> wraps - mirrors the
/// <see cref="IConsoleCommand"/>/<see cref="IConsoleAction"/> split exactly (docs/COMMAND_SURFACE_KEY_SPEC.md
/// MACROS §6): a Command is undoable editing state, an Action is a non-undoable runtime
/// operation. A Macro never converts one into the other.</summary>
public enum MacroStepKind
{
    Command,
    Action,
}

/// <summary>
/// One recorded console operation. For a Command (docs/COMMAND_SURFACE_KEY_SPEC.md MACROS
/// follow-up "REPLAY INSTANCE SAFETY"), <see cref="Command"/> is a TEMPLATE only - the exact
/// <see cref="IConsoleCommand"/> instance that was dispatched once at record time, kept purely
/// for inspection (type/DisplayLabel, and future EXAM). It must NEVER be dispatched again
/// directly: many commands hold mutable per-execution Undo snapshot state (e.g.
/// ProgrammerChannelCommandBase's own captured "before" values), so the SAME instance entering
/// the Undo stack twice would silently corrupt one execution's Undo with another's snapshot.
/// <see cref="CreateCommandForPlayback"/> is the only sanctioned way to get something to
/// actually dispatch - it always returns a brand-new instance via <see cref="IReplayableCommand.CreateFreshInstance"/>.
/// For an Action, <see cref="Action"/> IS dispatched directly - IConsoleAction implementations
/// hold no per-execution mutable state (they are never pushed onto the Undo stack in the first
/// place), so redispatching the same instance is inherently safe.
/// Exactly one of <see cref="Command"/>/<see cref="Action"/> is set, matching <see cref="Kind"/>.
/// Never a raw button click, Razor callback, or string - the same structured Application-layer
/// object every other Command Surface path already constructs and dispatches.
/// </summary>
public sealed class MacroStep
{
    public MacroStepKind Kind { get; }
    public IConsoleCommand? Command { get; }
    public IConsoleAction? Action { get; }

    private MacroStep(MacroStepKind kind, IConsoleCommand? command, IConsoleAction? action)
    {
        Kind = kind;
        Command = command;
        Action = action;
    }

    /// <summary><paramref name="command"/> must implement <see cref="IReplayableCommand"/> - see
    /// MacroRecorder's own recursive safety check (MacroRecorder decides whether a command
    /// qualifies at all; by the time this is called, it already has).</summary>
    public static MacroStep ForCommand(IConsoleCommand command)
    {
        if (command is not IReplayableCommand)
            throw new ArgumentException($"{command.GetType().Name} does not implement IReplayableCommand and cannot be safely recorded into a Macro.", nameof(command));
        return new(MacroStepKind.Command, command, null);
    }

    public static MacroStep ForAction(IConsoleAction action) => new(MacroStepKind.Action, null, action);

    /// <summary>Builds a brand-new, never-yet-executed IConsoleCommand instance for THIS specific
    /// playback - never <see cref="Command"/> (the template) itself, so its own Undo snapshot can
    /// never collide with any other playback's.</summary>
    public IConsoleCommand CreateCommandForPlayback() => ((IReplayableCommand)Command!).CreateFreshInstance();

    /// <summary>A short, honest, always-available label for future inspection (docs/COMMAND_SURFACE_KEY_SPEC.md
    /// MACROS §15's "EXAM MACRO 1" hook) - the step's own runtime type name, so Macro storage
    /// never needs a separate free-text description field per command/action type to remain
    /// inspectable.</summary>
    public string DisplayLabel => Kind == MacroStepKind.Command ? Command!.GetType().Name : Action!.GetType().Name;
}

/// <summary>Show data (docs/COMMAND_SURFACE_KEY_SPEC.md MACROS §10) - an ordered, named list of
/// recorded console operations bound to one fixed physical slot (1-8: MACRO 1-4, plus their
/// Shift-mapped MACRO 5-8 siblings). Lives on <see cref="ConsoleContext"/> via
/// <see cref="MacroBank"/>, not only in a Web ViewModel, so every client (touch, keyboard, a
/// future Natural Language layer) sees the same Macros, the same as PresetLibrary/GroupManager/
/// ExecutorBank already do.</summary>
public sealed class Macro
{
    public int Slot { get; }
    public string Name { get; set; }
    public List<MacroStep> Steps { get; } = new();

    public Macro(int slot, string? name = null)
    {
        Slot = slot;
        Name = name ?? $"Macro {slot}";
    }
}
