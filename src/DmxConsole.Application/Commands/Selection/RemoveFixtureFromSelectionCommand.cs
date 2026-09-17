using DmxConsole.Core.Fixtures;

namespace DmxConsole.Application.Commands.Selection;

/// <summary>Removes a single fixture from the current selection if present - a no-op (still
/// Success) if it wasn't selected. Symmetric counterpart to ToggleFixtureCommand, needed by the
/// command line's "-" (Minus) operator: unlike Toggle, "Fixture 1 Thru 10 - 5" must always
/// remove 5, never re-add it.</summary>
public sealed class RemoveFixtureFromSelectionCommand : SelectionCommandBase
{
    private readonly PatchedFixture _fixture;

    public RemoveFixtureFromSelectionCommand(PatchedFixture fixture) => _fixture = fixture;

    protected override ConsoleActionType ActionType => ConsoleActionType.RemoveFixtureFromSelection;

    protected override void Apply(ConsoleContext context) => context.Selection.Remove(_fixture);

    public override IConsoleCommand CreateFreshInstance() => new RemoveFixtureFromSelectionCommand(_fixture);
}
