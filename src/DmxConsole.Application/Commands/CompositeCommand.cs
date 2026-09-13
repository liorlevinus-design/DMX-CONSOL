namespace DmxConsole.Application.Commands;

/// <summary>
/// Wraps several commands so they execute, undo, and redo as one transaction - this is
/// what lets "lower Cold Wash and raise the Fronts" (two mutations from one utterance)
/// collapse into a single Undo. Re-executing on Redo lets each sub-command recapture a
/// fresh "previous state" snapshot for its own next Undo, so nesting/repeated undo-redo
/// stays correct.
/// </summary>
public sealed class CompositeCommand : IConsoleCommand
{
    private readonly IReadOnlyList<IConsoleCommand> _commands;

    public CompositeCommand(IReadOnlyList<IConsoleCommand> commands) => _commands = commands;

    public CommandResult Execute(ConsoleContext context)
    {
        CommandResult? last = null;
        foreach (var command in _commands)
        {
            last = command.Execute(context);
            if (!last.Success) break; // stop on first failure; already-executed sub-commands stay applied
        }

        return last ?? new CommandResult { ActionType = ConsoleActionType.Batch };
    }

    public void Undo(ConsoleContext context)
    {
        for (int i = _commands.Count - 1; i >= 0; i--) _commands[i].Undo(context);
    }
}
