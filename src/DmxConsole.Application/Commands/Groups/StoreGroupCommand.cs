using DmxConsole.Core.Selection;

namespace DmxConsole.Application.Commands.Groups;

/// <summary>
/// Creates a new Group at an explicit, operator-chosen Number, or re-stores (replaces the
/// membership and name of) an existing one - the "Store Group N" workflow the Groups View needs,
/// distinct from CreateGroupCommand's UI-only auto-numbered convenience path (kept as-is for the
/// classic SelectionBar "+Save as Group" flow, unchanged). Mirrors StorePresetCommand's create-
/// or-update shape: the caller decides which by passing (or omitting) an existingGroup.
/// </summary>
public sealed class StoreGroupCommand : IConsoleCommand, IHasUndoRisk
{
    private readonly string _name;
    private readonly int? _number;
    private readonly FixtureGroup? _existingGroup;

    private FixtureGroup? _target;
    private bool _wasNewlyCreated;
    private List<Core.Fixtures.PatchedFixture>? _previousMembers;
    private string? _previousName;

    public StoreGroupCommand(string name, int? number = null, FixtureGroup? existingGroup = null)
    {
        _name = name;
        _number = number;
        _existingGroup = existingGroup;
    }

    public CommandResult Execute(ConsoleContext context)
    {
        if (context.Selection.Items.Count == 0)
            return CommandResult.Failed(ConsoleActionType.StoreGroup, "Nothing selected to store as a group.");

        _wasNewlyCreated = _existingGroup is null;

        if (_wasNewlyCreated)
        {
            // An explicit, operator-chosen number that's already taken is a hard conflict, never
            // a silent fallback to some other number - same rule this milestone just applied to
            // Patch's fixture numbering.
            if (_number is > 0 && context.Groups.FindByNumber(_number.Value) is not null)
                return CommandResult.Failed(ConsoleActionType.StoreGroup, $"Group #{_number} is already in use - choose a different number.");

            _target = context.Groups.CreateFromSelection(_name, context.Selection, _number);
        }
        else
        {
            _target = _existingGroup!;
            _previousMembers = new List<Core.Fixtures.PatchedFixture>(_target.Fixtures);
            _previousName = _target.Name;
            _target.Fixtures.Clear();
            _target.Fixtures.AddRange(context.Selection.Items);
            _target.Name = _name;
        }

        return new CommandResult
        {
            ActionType = ConsoleActionType.StoreGroup,
            AffectedFixtures = _target.Fixtures,
            Group = _target,
        };
    }

    public void Undo(ConsoleContext context)
    {
        if (_target is null) return; // Execute failed or was never called

        if (_wasNewlyCreated)
        {
            context.Groups.Remove(_target);
        }
        else
        {
            _target.Fixtures.Clear();
            _target.Fixtures.AddRange(_previousMembers!);
            _target.Name = _previousName!;
        }
    }

    /// <summary>Only the "was new" branch is destructive - same rule as StorePresetCommand/
    /// CreateGroupCommand/CreateExecutorCommand.</summary>
    public UndoProposal PrepareUndo()
    {
        if (!_wasNewlyCreated) return UndoProposal.SingleSafe($"Restore previous members/name of Group \"{_name}\"");

        var description = $"Delete Group \"{_name}\"";
        return new UndoProposal(description, new[] { new UndoOption("delete", description, UndoRisk.Destructive) });
    }
}
