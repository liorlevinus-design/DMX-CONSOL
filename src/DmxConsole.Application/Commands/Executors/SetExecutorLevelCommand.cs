using DmxConsole.Core.Engine;

namespace DmxConsole.Application.Commands.Executors;

/// <summary>Sets an Executor's FaderLevel (e.g. "Executor 3 At 50") - Safe: reverting a level is
/// never destructive.</summary>
public sealed class SetExecutorLevelCommand : IConsoleCommand
{
    private readonly Executor _executor;
    private readonly double _level;
    private double _previousLevel;

    public SetExecutorLevelCommand(Executor executor, double level)
    {
        _executor = executor;
        _level = level;
    }

    public CommandResult Execute(ConsoleContext context)
    {
        _previousLevel = _executor.FaderLevel;
        _executor.FaderLevel = _level;
        return new CommandResult { ActionType = ConsoleActionType.SetExecutorLevel, Executor = _executor };
    }

    public void Undo(ConsoleContext context) => _executor.FaderLevel = _previousLevel;
}
