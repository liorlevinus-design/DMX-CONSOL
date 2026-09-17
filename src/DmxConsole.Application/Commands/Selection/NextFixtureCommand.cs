namespace DmxConsole.Application.Commands.Selection;

/// <summary>Replaces the selection with the single fixture after the current one (by Number), wrapping at the end.</summary>
public sealed class NextFixtureCommand : SelectionCommandBase
{
    protected override ConsoleActionType ActionType => ConsoleActionType.Next;

    protected override void Apply(ConsoleContext context) => context.Selection.Next(context.Patch);

    public override IConsoleCommand CreateFreshInstance() => new NextFixtureCommand();
}
