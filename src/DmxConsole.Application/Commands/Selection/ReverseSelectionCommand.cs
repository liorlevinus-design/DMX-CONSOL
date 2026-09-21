namespace DmxConsole.Application.Commands.Selection;

/// <summary>Reverses the order of the current selection - added alongside SelectOdd/SelectEven,
/// same shape, so the Selection History rule's "any future Selection filter/transform" claim has
/// a real second transform to prove itself against, not just Odd/Even.</summary>
public sealed class ReverseSelectionCommand : SelectionCommandBase
{
    protected override ConsoleActionType ActionType => ConsoleActionType.ReverseSelection;

    protected override void Apply(ConsoleContext context) => context.Selection.Reverse();

    public override IConsoleCommand CreateFreshInstance() => new ReverseSelectionCommand();
}
