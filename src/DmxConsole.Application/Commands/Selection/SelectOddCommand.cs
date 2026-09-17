namespace DmxConsole.Application.Commands.Selection;

/// <summary>Keeps only the fixtures at odd 1-based positions in the current selection order.</summary>
public sealed class SelectOddCommand : SelectionCommandBase
{
    protected override ConsoleActionType ActionType => ConsoleActionType.SelectOdd;

    protected override void Apply(ConsoleContext context) => context.Selection.FilterOdd();

    public override IConsoleCommand CreateFreshInstance() => new SelectOddCommand();
}
