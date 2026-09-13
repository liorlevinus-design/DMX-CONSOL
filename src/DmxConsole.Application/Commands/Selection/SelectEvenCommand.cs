namespace DmxConsole.Application.Commands.Selection;

/// <summary>Keeps only the fixtures at even 1-based positions in the current selection order.</summary>
public sealed class SelectEvenCommand : SelectionCommandBase
{
    protected override ConsoleActionType ActionType => ConsoleActionType.SelectEven;

    protected override void Apply(ConsoleContext context) => context.Selection.FilterEven();
}
