using DmxConsole.Core.Engine;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Application.Commands.Cues;

/// <summary>
/// CUE &lt;n&gt; [THRU &lt;n&gt;] TIME &lt;value&gt;[/&lt;value&gt;] grammar (Cue-timing slice) -
/// the ONE shared Application operation the Command Surface's TIME grammar dispatches through to
/// edit an already-recorded Cue's flat In/Out fade timing, via <see cref="CueList.SetTiming"/>.
/// One instance edits exactly one Cue; a "THRU" range is composed as several of these inside one
/// <see cref="CompositeCommand"/> by CommandComposer, so the whole range update is a single atomic
/// Undo step (CLAUDE.md §14), never N separate Undo entries.
///
/// Only TimeIn/TimeOut are ever touched here - DelayIn/DelayOut are read from the Cue's existing
/// Timing and carried through unchanged (this grammar slice never invents Delay grammar). Never
/// touches TriggerMode/WaitTime - Cue Trigger Semantics (CLAUDE.md §9) are a completely separate
/// concern, deliberately never read or written by this command. Never introduces any
/// AttributeClass-level/per-family timing storage - see CLAUDE.md's "Known contradiction - do not
/// reintroduce" callout; this is cue-level timing only, exactly like the Timing property it edits.
///
/// Always Safe on Undo - like RenameGroupCommand/SetTriggerMode's own callers, this only restores
/// the Cue's previous Timing value, it never deletes or creates anything.
/// </summary>
public sealed class SetCueTimingCommand : IConsoleCommand
{
    private readonly CueList _cueList;
    private readonly Cue _cue;
    private readonly TimeSpan _timeIn;
    private readonly TimeSpan _timeOut;

    private CueTiming? _previousTiming;

    public SetCueTimingCommand(CueList cueList, Cue cue, TimeSpan timeIn, TimeSpan timeOut)
    {
        _cueList = cueList;
        _cue = cue;
        _timeIn = timeIn;
        _timeOut = timeOut;
    }

    public CommandResult Execute(ConsoleContext context)
    {
        _previousTiming = _cue.Timing;
        var newTiming = _cue.Timing with { TimeIn = _timeIn, TimeOut = _timeOut };

        if (!_cueList.SetTiming(_cue, newTiming))
        {
            return CommandResult.Failed(ConsoleActionType.SetCueTiming,
                $"Cue {_cue.Number:0.##} is no longer in the Cue List.");
        }

        return new CommandResult
        {
            ActionType = ConsoleActionType.SetCueTiming,
            AffectedFixtures = Array.Empty<PatchedFixture>(),
            Cue = _cue,
        };
    }

    public void Undo(ConsoleContext context)
    {
        if (_previousTiming is { } previous) _cueList.SetTiming(_cue, previous);
    }
}
