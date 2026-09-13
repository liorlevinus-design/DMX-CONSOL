using DmxConsole.Core.Selection;

namespace DmxConsole.Application.Commands.Groups;

/// <summary>Saves the current selection's members as a new named Group.</summary>
public sealed class CreateGroupCommand : IConsoleCommand
{
    private readonly string _name;
    private FixtureGroup? _created;

    public CreateGroupCommand(string name) => _name = name;

    public CommandResult Execute(ConsoleContext context)
    {
        _created = context.Groups.CreateFromSelection(_name, context.Selection);

        return new CommandResult
        {
            ActionType = ConsoleActionType.CreateGroup,
            AffectedFixtures = _created.Fixtures,
            Group = _created,
        };
    }

    public void Undo(ConsoleContext context)
    {
        if (_created is not null) context.Groups.Remove(_created);
    }
}
