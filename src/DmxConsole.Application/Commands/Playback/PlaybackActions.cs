using DmxConsole.Core.Engine;

namespace DmxConsole.Application.Commands.Playback;

/// <summary>
/// Operational/runtime Actions (IConsoleAction - never enter Undo history, see IConsoleAction).
/// Each is a thin proxy to the matching Executor method, returning CommandResult.Failed
/// gracefully (never throwing) if the assigned Source doesn't support the capability.
/// </summary>
public sealed class GoAction : IConsoleAction
{
    private readonly Executor _executor;
    public GoAction(Executor executor) => _executor = executor;

    public CommandResult Execute(ConsoleContext context)
    {
        if (_executor.Source is not ISequencedPlayback)
            return CommandResult.Failed(ConsoleActionType.Go, "This Executor's Source doesn't support Go.");

        _executor.Go();
        return new CommandResult { ActionType = ConsoleActionType.Go, Executor = _executor };
    }
}

public sealed class BackAction : IConsoleAction
{
    private readonly Executor _executor;
    public BackAction(Executor executor) => _executor = executor;

    public CommandResult Execute(ConsoleContext context)
    {
        if (_executor.Source is not ISequencedPlayback)
            return CommandResult.Failed(ConsoleActionType.Back, "This Executor's Source doesn't support Back.");

        _executor.Back();
        return new CommandResult { ActionType = ConsoleActionType.Back, Executor = _executor };
    }
}

public sealed class StopAction : IConsoleAction
{
    private readonly Executor _executor;
    public StopAction(Executor executor) => _executor = executor;

    public CommandResult Execute(ConsoleContext context)
    {
        if (_executor.Source is not ISequencedPlayback)
            return CommandResult.Failed(ConsoleActionType.Stop, "This Executor's Source doesn't support Stop.");

        _executor.Stop();
        return new CommandResult { ActionType = ConsoleActionType.Stop, Executor = _executor };
    }
}

public sealed class PauseAction : IConsoleAction
{
    private readonly Executor _executor;
    public PauseAction(Executor executor) => _executor = executor;

    public CommandResult Execute(ConsoleContext context)
    {
        if (_executor.Source is not IPausablePlayback)
            return CommandResult.Failed(ConsoleActionType.Pause, "This Executor's Source doesn't support Pause.");

        _executor.Pause();
        return new CommandResult { ActionType = ConsoleActionType.Pause, Executor = _executor };
    }
}

public sealed class ResumeAction : IConsoleAction
{
    private readonly Executor _executor;
    public ResumeAction(Executor executor) => _executor = executor;

    public CommandResult Execute(ConsoleContext context)
    {
        if (_executor.Source is not IPausablePlayback)
            return CommandResult.Failed(ConsoleActionType.Resume, "This Executor's Source doesn't support Resume.");

        _executor.Resume();
        return new CommandResult { ActionType = ConsoleActionType.Resume, Executor = _executor };
    }
}

/// <summary>Dispatched on press (mode Add) - see FlashReleaseAction for release.</summary>
public sealed class FlashPressAction : IConsoleAction
{
    private readonly Executor _executor;
    public FlashPressAction(Executor executor) => _executor = executor;

    public CommandResult Execute(ConsoleContext context)
    {
        if (!_executor.TrySetFlash(FlashMode.Add))
            return CommandResult.Failed(ConsoleActionType.FlashPress, "Flash mode not supported.");

        return new CommandResult { ActionType = ConsoleActionType.FlashPress, Executor = _executor };
    }
}

/// <summary>Dispatched on release - two separate Actions (press/release), not one Undo-paired
/// pulse, exactly like the existing ArmThru UI-only pattern from Step C0.</summary>
public sealed class FlashReleaseAction : IConsoleAction
{
    private readonly Executor _executor;
    public FlashReleaseAction(Executor executor) => _executor = executor;

    public CommandResult Execute(ConsoleContext context)
    {
        _executor.TrySetFlash(FlashMode.None); // None is always accepted - never fails
        return new CommandResult { ActionType = ConsoleActionType.FlashRelease, Executor = _executor };
    }
}
