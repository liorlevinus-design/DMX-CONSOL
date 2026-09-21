using DmxConsole.Core.Engine;

namespace DmxConsole.Application.Commands.Cues;

/// <summary>
/// STORE CUE n grammar (Store-grammar slice) - the ONE shared Application operation both the
/// Cues panel and the Command Surface's STORE grammar dispatch through, replacing the panel's
/// former direct <see cref="CueList.RecordCue"/> call. Create-only, mirroring the Cues panel's
/// own existing "Store" semantics exactly (CueListViewModel.StoreWithFilter): if a Cue already
/// exists at this number, this fails and tells the operator to Update it instead - it never
/// silently updates. Timing/Trigger/StoreFilter are supplied by the caller (today, whatever the
/// Cues panel/shared cue state currently holds) - this command invents no new grammar for them.
/// </summary>
public sealed class StoreCueCommand : IConsoleCommand, IHasUndoRisk
{
    private readonly CueList _cueList;
    private readonly string _name;
    private readonly double _number;
    private readonly CueStoreOptions _options;

    private Cue? _created;

    public StoreCueCommand(CueList cueList, string name, double number, CueStoreOptions options)
    {
        _cueList = cueList;
        _name = name;
        _number = number;
        _options = options;
    }

    public CommandResult Execute(ConsoleContext context)
    {
        if (_cueList.FindByNumber(_number) is not null)
        {
            return CommandResult.Failed(ConsoleActionType.StoreCue,
                $"Cue {_number:0.##} already exists - Update it instead, or choose a different number.");
        }

        _created = _cueList.RecordCue(context.Patch, context.Programmer, context.Selection, context.EffectiveOutput, _name, _number, _options);

        return new CommandResult
        {
            ActionType = ConsoleActionType.StoreCue,
            AffectedFixtures = context.Selection.Items.ToList(),
            Cue = _created,
        };
    }

    public void Undo(ConsoleContext context)
    {
        if (_created is null) return; // Execute failed or was never called
        _cueList.RemoveCue(_created);
    }

    /// <summary>Always destructive - Undo deletes the freshly-created Cue, same rule as
    /// StoreGroupCommand/StorePresetCommand's "was new" branch (this command never updates an
    /// existing Cue, so there is no "was update" branch to be Safe).</summary>
    public UndoProposal PrepareUndo()
    {
        var description = $"Delete Cue {_number:0.##} \"{_name}\"";
        return new UndoProposal(description, new[] { new UndoOption("delete", description, UndoRisk.Destructive) });
    }
}
