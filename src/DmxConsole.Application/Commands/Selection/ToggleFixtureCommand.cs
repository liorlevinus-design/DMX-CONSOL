using DmxConsole.Core.Fixtures;

namespace DmxConsole.Application.Commands.Selection;

/// <summary>Toggles a single fixture in or out of the current selection.</summary>
public sealed class ToggleFixtureCommand : SelectionCommandBase
{
    private readonly PatchedFixture _fixture;

    public ToggleFixtureCommand(PatchedFixture fixture) => _fixture = fixture;

    protected override ConsoleActionType ActionType => ConsoleActionType.ToggleFixture;

    protected override void Apply(ConsoleContext context) => context.Selection.Toggle(_fixture);

    public override IConsoleCommand CreateFreshInstance() => new ToggleFixtureCommand(_fixture);
}
