using DmxConsole.Core.Selection;

namespace DmxConsole.Application.Commands.Selection;

/// <summary>Adds every member of a saved Group to the current selection.</summary>
public sealed class AddGroupToSelectionCommand : SelectionCommandBase
{
    private readonly FixtureGroup _group;

    public AddGroupToSelectionCommand(FixtureGroup group) => _group = group;

    protected override ConsoleActionType ActionType => ConsoleActionType.AddGroupToSelection;

    protected override void Apply(ConsoleContext context) => context.Selection.AddGroup(_group);
}
