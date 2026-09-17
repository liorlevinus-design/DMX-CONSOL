namespace DmxConsole.Application.Commands.Selection;

/// <summary>Empties the current selection.</summary>
public sealed class ClearSelectionCommand : SelectionCommandBase
{
    protected override ConsoleActionType ActionType => ConsoleActionType.ClearSelection;

    protected override void Apply(ConsoleContext context) => context.Selection.Clear();

    public override IConsoleCommand CreateFreshInstance() => new ClearSelectionCommand();
}
