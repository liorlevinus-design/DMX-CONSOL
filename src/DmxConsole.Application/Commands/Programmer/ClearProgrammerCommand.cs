namespace DmxConsole.Application.Commands.Programmer;

/// <summary>
/// Undoably clears the ENTIRE Programmer - values, knockout, AND per-channel timing overrides
/// (<see cref="DmxConsole.Core.Engine.Programmer.ClearAll"/>) - as one step of a larger
/// transaction. Built for CLAUDE.md §5's N1 rule ("after a successful STORE CUE, the entire
/// Programmer is cleared automatically... the Store and the Programmer clear are one atomic,
/// undoable transaction"): every Cue-store path (new/UPDATE/OVERWRITE) wraps
/// [Cue-mutation-command, ClearProgrammerCommand] in a <see cref="CompositeCommand"/> so ONE
/// Undo restores both the Cue and the full Programmer state.
///
/// Deliberately NOT used by RELEASE RELEASE (ReleaseCommand's own global-release path) - that is
/// a separate, pre-existing code path with its own known (and explicitly out-of-scope-for-N1)
/// timing-clear gap. This command exists purely for the Cue-store transaction described above.
/// </summary>
public sealed class ClearProgrammerCommand : IConsoleCommand
{
    private IReadOnlyDictionary<(int Universe, int Channel), byte>? _previousValues;
    private IReadOnlySet<(int Universe, int Channel)>? _previousKnockout;
    private IReadOnlyDictionary<(int Universe, int Channel), (TimeSpan? TimeIn, TimeSpan? TimeOut)>? _previousTiming;
    private bool _executed;

    public CommandResult Execute(ConsoleContext context)
    {
        _previousValues = context.Programmer.Snapshot();
        _previousKnockout = context.Programmer.KnockoutSnapshot();
        _previousTiming = context.Programmer.TimingSnapshot();
        context.Programmer.ClearAll();
        _executed = true;

        return new CommandResult { ActionType = ConsoleActionType.ClearProgrammer };
    }

    public void Undo(ConsoleContext context)
    {
        if (!_executed) return; // Execute failed or was never called

        foreach (var (key, value) in _previousValues!)
            context.Programmer.SetChannel(key.Universe, key.Channel, value);

        foreach (var key in _previousKnockout!)
            context.Programmer.Knockout(key.Universe, key.Channel);

        foreach (var (key, timing) in _previousTiming!)
            context.Programmer.SetTimingRaw(key.Universe, key.Channel, timing.TimeIn, timing.TimeOut);
    }
}
