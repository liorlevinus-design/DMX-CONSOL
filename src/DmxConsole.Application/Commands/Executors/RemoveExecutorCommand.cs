using DmxConsole.Core.Engine;

namespace DmxConsole.Application.Commands.Executors;

/// <summary>Removes an Executor. Undo re-inserts it at (as close as possible to) its original
/// position - Safe: Undo here *restores* a removed object, the opposite direction from Create,
/// never destructive.</summary>
public sealed class RemoveExecutorCommand : IConsoleCommand
{
    private readonly Executor _executor;
    private int _previousIndex = -1;

    public RemoveExecutorCommand(Executor executor) => _executor = executor;

    public CommandResult Execute(ConsoleContext context)
    {
        _previousIndex = context.Executors.Executors.IndexOf(_executor);
        if (_previousIndex < 0)
        {
            return CommandResult.Failed(ConsoleActionType.RemoveExecutor,
                $"Executor {_executor.Number} is not in the current Executor bank.");
        }

        context.Executors.Remove(_executor);

        return new CommandResult { ActionType = ConsoleActionType.RemoveExecutor, Executor = _executor };
    }

    public void Undo(ConsoleContext context)
    {
        if (_previousIndex < 0) return;

        int index = Math.Min(_previousIndex, context.Executors.Executors.Count);
        context.Executors.Executors.Insert(index, _executor);
    }
}
