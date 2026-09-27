using DmxConsole.Core.Engine;

namespace DmxConsole.Application.Commands.Cues;

/// <summary>
/// UPDATE / OVERWRITE for an already-recorded Cue (CLAUDE.md §5 C7's generic Store-conflict
/// terminology) - the real, undoable Application command the Cues panel's "Update" button and
/// the Command Surface's UPDATE key now dispatch through, replacing the former direct
/// <see cref="CueList.UpdateCue"/> call from the ViewModel. Mirrors StorePresetCommand's shape:
/// one command class, an <c>overwrite</c> flag distinguishing the two conflict-resolution
/// outcomes, both applied to an already-existing target (never the "was newly created" case -
/// that remains <see cref="StoreCueCommand"/>'s job, which hard-fails on a collision instead).
///
/// IMPORTANT (N1 slice audit finding): <see cref="CueList.UpdateCue"/> has never implemented a
/// Preset-style dictionary MERGE against the existing Cue's Levels. <c>BuildCue</c> always
/// produces a brand-new, complete Levels dictionary from the CURRENT live Patch/Programmer/
/// Selection state (scoped by the given <see cref="CueStoreOptions"/>.Filter), and
/// <see cref="CueList.UpdateCue"/> simply swaps the old Cue object out for that new one - it has
/// always been a full replace, never a merge. Per explicit product direction, this command
/// reproduces that exact OUTCOME rather than introducing new dictionary-merge behavior.
/// Consequently, with today's data model (full-snapshot recording, no per-channel Tracking yet -
/// see CueList's own class doc comment), UPDATE (<c>overwrite: false</c>) and OVERWRITE
/// (<c>overwrite: true</c>) produce an IDENTICAL result: both replace the Cue's entire Levels with
/// a fresh BuildCue snapshot. OVERWRITE still exists as its own distinct, dispatchable outcome
/// (never collapsed into UPDATE, per CLAUDE.md §5's "exactly three options, never renamed" rule)
/// so that if/when per-channel Tracking arrives and BuildCue starts producing a genuinely PARTIAL
/// Levels set, UPDATE can be widened to merge that partial set into the previous Cue's Levels
/// while OVERWRITE keeps today's full-replace meaning - without any caller of this command
/// needing to change.
/// </summary>
public sealed class UpdateCueCommand : IConsoleCommand, IHasUndoRisk
{
    private readonly CueList _cueList;
    private readonly Cue _existing;
    private readonly string _name;
    private readonly CueStoreOptions _options;
    private readonly bool _overwrite;

    private Cue? _updated;

    public UpdateCueCommand(CueList cueList, Cue existing, string name, CueStoreOptions options, bool overwrite = false)
    {
        _cueList = cueList;
        _existing = existing;
        _name = name;
        _options = options;
        _overwrite = overwrite;
    }

    public CommandResult Execute(ConsoleContext context)
    {
        var actionType = _overwrite ? ConsoleActionType.OverwriteCue : ConsoleActionType.UpdateCue;

        _updated = _cueList.UpdateCue(_existing, context.Patch, context.Programmer, context.Selection, context.EffectiveOutput, _name, _options);
        if (_updated is null)
        {
            return CommandResult.Failed(actionType, $"Cue {_existing.Number:0.##} is no longer in this list.");
        }

        return new CommandResult
        {
            ActionType = actionType,
            AffectedFixtures = context.Selection.Items.ToList(),
            Cue = _updated,
        };
    }

    public void Undo(ConsoleContext context)
    {
        if (_updated is null) return; // Execute failed or was never called

        // _existing was never mutated by Execute (only swapped out) - putting it back in
        // _updated's slot restores the Cue's exact previous content, verbatim.
        _cueList.ReplaceCue(_updated, _existing);
    }

    /// <summary>Never destructive - Undo restores the exact original Cue object (never mutated,
    /// only swapped out by Execute), the same "was update" Safe pattern StorePresetCommand uses
    /// for its own update branch.</summary>
    public UndoProposal PrepareUndo() =>
        UndoProposal.SingleSafe($"Restore previous content of Cue {_existing.Number:0.##} \"{_existing.Name}\"");
}
