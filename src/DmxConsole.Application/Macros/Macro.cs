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

/// <summary>One recorded console operation - the exact <see cref="IConsoleCommand"/> or
/// <see cref="IConsoleAction"/> instance that was already dispatched once when it was recorded
/// (see <see cref="MacroRecorder"/>). Exactly one of <see cref="Command"/>/<see cref="Action"/>
/// is set, matching <see cref="Kind"/>. Never a raw button click, Razor callback, or string -
/// the same structured Application-layer object every other Command Surface path already
/// constructs and dispatches.</summary>
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

    public static MacroStep ForCommand(IConsoleCommand command) => new(MacroStepKind.Command, command, null);
    public static MacroStep ForAction(IConsoleAction action) => new(MacroStepKind.Action, null, action);

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
