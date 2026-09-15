using DmxConsole.Core.Selection;

namespace DmxConsole.Application.Commands.Groups;

/// <summary>Renames a Group without touching its membership. Always Safe - Undo just restores the previous name.</summary>
public sealed class RenameGroupCommand : IConsoleCommand
{
    private readonly FixtureGroup _group;
    private readonly string _newName;
    private string? _previousName;

    public RenameGroupCommand(FixtureGroup group, string newName)
    {
        _group = group;
        _newName = newName;
    }

    public CommandResult Execute(ConsoleContext context)
    {
        _previousName = _group.Name;
        _group.Name = _newName;

        return new CommandResult
        {
            ActionType = ConsoleActionType.RenameGroup,
            AffectedFixtures = _group.Fixtures,
            Group = _group,
        };
    }

    public void Undo(ConsoleContext context)
    {
        if (_previousName is not null) _group.Name = _previousName;
    }
}
