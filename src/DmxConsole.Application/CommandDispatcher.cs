using DmxConsole.Application.Commands;

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

    public CommandDispatcher(ConsoleContext context, UndoRedoService undoRedo)
    {
        _context = context;
        _undoRedo = undoRedo;
    }

    public CommandResult Dispatch(IConsoleCommand command)
    {
        var result = command.Execute(_context);
        if (result.Success) _undoRedo.Push(command);
        return result;
    }

    /// <summary>Executes several commands as one transaction - a single Undo() reverts all of them.</summary>
    public CommandResult DispatchBatch(IReadOnlyList<IConsoleCommand> commands) =>
        Dispatch(new CompositeCommand(commands));
}
