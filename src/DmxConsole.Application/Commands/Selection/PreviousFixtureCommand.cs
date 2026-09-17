namespace DmxConsole.Application.Commands.Selection;

/// <summary>Replaces the selection with the single fixture before the current one (by Number), wrapping at the start.</summary>
public sealed class PreviousFixtureCommand : SelectionCommandBase
{
    protected override ConsoleActionType ActionType => ConsoleActionType.Previous;

    protected override void Apply(ConsoleContext context) => context.Selection.Previous(context.Patch);

    public override IConsoleCommand CreateFreshInstance() => new PreviousFixtureCommand();
}
