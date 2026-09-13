using DmxConsole.Core.Selection;

namespace DmxConsole.Application.Commands.Groups;

/// <summary>Removes a saved Group. Undo re-inserts it at (as close as possible to) its original position.</summary>
public sealed class RemoveGroupCommand : IConsoleCommand
{
    private readonly FixtureGroup _group;
    private int _previousIndex = -1;

    public RemoveGroupCommand(FixtureGroup group) => _group = group;

    public CommandResult Execute(ConsoleContext context)
    {
        _previousIndex = context.Groups.Groups.IndexOf(_group);
        if (_previousIndex < 0)
        {
            return CommandResult.Failed(ConsoleActionType.RemoveGroup,
                $"Group '{_group.Name}' is not in the current group list.");
        }

        context.Groups.Remove(_group);

        return new CommandResult
        {
            ActionType = ConsoleActionType.RemoveGroup,
            AffectedFixtures = _group.Fixtures,
            Group = _group,
        };
    }

    public void Undo(ConsoleContext context)
    {
        if (_previousIndex < 0) return; // Execute failed or was never called - nothing to undo

        int index = Math.Min(_previousIndex, context.Groups.Groups.Count);
        context.Groups.Groups.Insert(index, _group);
    }
}
