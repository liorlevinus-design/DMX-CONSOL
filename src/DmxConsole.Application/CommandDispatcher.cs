using DmxConsole.Application.Commands;
using DmxConsole.Application.Commands.Selection;

namespace DmxConsole.Application;

/// <summary>
/// The single entry point for every mutation to the console. Any client - the Blazor UI,
/// a macro, a MIDI/OSC trigger, or eventually a Natural Language layer - talks to the
/// console exclusively through this, never by touching Core objects directly.
/// </summary>
public sealed class CommandDispatcher
{
    private readonly ConsoleContext _context;
    private readonly UndoRedoService _undoRedo;

    /// <summary>Fired right after a successful IConsoleCommand.Execute (right after it's pushed
    /// onto the Undo stack) - the hook Macros/MacroRecorder attaches to while LEARN MACRO is
    /// recording (docs/COMMAND_SURFACE_KEY_SPEC.md MACROS slice). Never fires for a failed
    /// dispatch - nothing happened, nothing to record.</summary>
    public event Action<IConsoleCommand>? CommandExecuted;

    /// <summary>Same as <see cref="CommandExecuted"/>, for a successful IConsoleAction - fires
    /// independently of Undo, since Actions never touch UndoRedoService (see DispatchAction's own
    /// doc comment).</summary>
    public event Action<IConsoleAction>? ActionExecuted;

    public CommandDispatcher(ConsoleContext context, UndoRedoService undoRedo)
    {
        _context = context;
        _undoRedo = undoRedo;
    }

    public CommandResult Dispatch(IConsoleCommand command)
    {
        var result = command.Execute(_context);
        if (result.Success)
        {
            _undoRedo.Push(command);
            CommandExecuted?.Invoke(command);

            // Selection History rule: the ONE place SelectionCycle.LastSelection is updated,
            // driven entirely by SelectionCommandBase.ProducesSelectionSnapshot (checked
            // recursively through CompositeCommand, since a selection-clause command line is
            // usually dispatched as one composite) - never by individual callers remembering to
            // call RememberSelection themselves. This is what makes the invariant universal: it
            // does not matter whether the command came from Command Surface grammar, a GUI
            // fixture click, a Group recall, Odd/Even/Reverse, or any future selection transform -
            // if it is (or contains) a SelectionCommandBase whose ProducesSelectionSnapshot is
            // true, its result becomes the new Last Selection. CLEAR is excluded structurally,
            // twice over: ClearSelectionCommand overrides ProducesSelectionSnapshot to false, and
            // the CLEAR key itself dispatches ClearSelectionAction (an IConsoleAction) through
            // DispatchAction below, which never reaches this method at all.
            if (ProducesSelectionSnapshot(command))
                _context.SelectionCycle.RememberSelection(_context.Selection.Items);
        }
        return result;
    }

    private static bool ProducesSelectionSnapshot(IConsoleCommand command) => command switch
    {
        SelectionCommandBase selectionCommand => selectionCommand.ProducesSelectionSnapshot,
        CompositeCommand composite => composite.Commands.Any(ProducesSelectionSnapshot),
        _ => false,
    };

    /// <summary>Executes several commands as one transaction - a single Undo() reverts all of them.</summary>
    public CommandResult DispatchBatch(IReadOnlyList<IConsoleCommand> commands) =>
        Dispatch(new CompositeCommand(commands));

    /// <summary>Executes an operational/runtime Action - Go/Back/Stop/Pause/Resume/Flash. Never
    /// touches UndoRedoService (no push, no clear) - this is the structural guarantee that
    /// operational actions can never enter Undo history and can never clear the Redo stack.</summary>
    public CommandResult DispatchAction(IConsoleAction action)
    {
        var result = action.Execute(_context);
        if (result.Success) ActionExecuted?.Invoke(action);
        return result;
    }
}
