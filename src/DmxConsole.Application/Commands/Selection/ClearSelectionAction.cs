namespace DmxConsole.Application.Commands.Selection;

/// <summary>
/// CLEAR (docs/COMMAND_SURFACE_KEY_SPEC.md §15, redefined for this slice): immediately clears the
/// CURRENT Fixture Selection and nothing else.
///
/// Deliberately an <see cref="IConsoleAction"/> rather than an <see cref="IConsoleCommand"/>:
/// clearing the selection is an operator navigation gesture, so it must never enter Undo history
/// and must never clear the Redo stack (see IConsoleAction's own doc comment - that exclusion is
/// structural, enforced by CommandDispatcher.DispatchAction, not by convention).
///
/// It touches exactly one thing - <see cref="ConsoleContext.Selection"/>. It never touches
/// Programmer/Editor values, never touches the shared EditorContextStack, never touches the
/// Command Surface's partial command-line composition or pending digits, and never touches
/// unrelated interaction state (a live Shift arm, LEARN MACRO arm/recording, or RELEASE-ENTER arm
/// is left exactly as it was). It has no second-press/escalation behavior of any kind: pressing
/// CLEAR again simply clears an already-empty selection again. The command line's own editing
/// (removing tokens) belongs to Backspace, not to CLEAR.
///
/// It is also deliberately EXCLUDED from Macro recording - see MacroRecorder.OnActionExecuted:
/// clearing the operating selection is a navigation gesture, never a step worth replaying.
/// </summary>
public sealed class ClearSelectionAction : IConsoleAction
{
    public CommandResult Execute(ConsoleContext context)
    {
        context.Selection.Clear();
        return new CommandResult { ActionType = ConsoleActionType.ClearSelection };
    }
}
