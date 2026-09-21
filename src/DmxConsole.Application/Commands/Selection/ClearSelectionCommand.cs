namespace DmxConsole.Application.Commands.Selection;

/// <summary>Empties the current selection.</summary>
public sealed class ClearSelectionCommand : SelectionCommandBase
{
    protected override ConsoleActionType ActionType => ConsoleActionType.ClearSelection;

    /// <summary>Selection History rule's deliberate exception: clearing must never become the new
    /// SelectionCycle.LastSelection to recall (see SelectionCommandBase.ProducesSelectionSnapshot's
    /// own doc comment) - "CLEAR clears Current Selection, does NOT overwrite Last Selection".
    /// Today this command is only ever dispatched bundled inside a CompositeCommand together with
    /// a real follow-up selection command (CommandComposer.BuildSelectionCommands,
    /// SelectionViewModel.DispatchSelectionGesture, GroupsViewModel.Apply all do this - never
    /// standalone), so in practice the composite's OTHER member already supplies the correct
    /// snapshot; this override exists so the rule holds even if that ever changes.</summary>
    protected internal override bool ProducesSelectionSnapshot => false;

    protected override void Apply(ConsoleContext context) => context.Selection.Clear();

    public override IConsoleCommand CreateFreshInstance() => new ClearSelectionCommand();
}
