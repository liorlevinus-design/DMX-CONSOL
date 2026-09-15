using DmxConsole.Core.Fixtures;

namespace DmxConsole.Application.Commands.Selection;

/// <summary>
/// Adds one fixture to the ordered selection without toggling it off when it is already present.
/// Command-surface Anchor/Plus clauses use this rather than ToggleFixtureCommand so a command
/// composed from an already-visible GUI selection can safely be followed by AT/other actions
/// without accidentally deselecting its own targets.
/// </summary>
public sealed class AddFixtureToSelectionCommand : SelectionCommandBase
{
    private readonly PatchedFixture _fixture;

    public AddFixtureToSelectionCommand(PatchedFixture fixture) => _fixture = fixture;

    protected override ConsoleActionType ActionType => ConsoleActionType.ToggleFixture;

    protected override void Apply(ConsoleContext context) => context.Selection.Add(_fixture);
}
