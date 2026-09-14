using DmxConsole.Core.Engine;

namespace DmxConsole.Application.Commands.Executors;

/// <summary>Assigns (or reassigns) a Playback Source onto an Executor - Safe: restoring a
/// reference is never destructive, even when the previous value was "empty".</summary>
public sealed class AssignExecutorCommand : IConsoleCommand
{
    private readonly Executor _executor;
    private readonly IPlaybackSource? _source;
    private IPlaybackSource? _previousSource;

    public AssignExecutorCommand(Executor executor, IPlaybackSource? source)
    {
        _executor = executor;
        _source = source;
    }

    public CommandResult Execute(ConsoleContext context)
    {
        _previousSource = _executor.Source;
        _executor.Assign(_source);
        return new CommandResult { ActionType = ConsoleActionType.AssignExecutor, Executor = _executor };
    }

    public void Undo(ConsoleContext context) => _executor.Assign(_previousSource);
}
