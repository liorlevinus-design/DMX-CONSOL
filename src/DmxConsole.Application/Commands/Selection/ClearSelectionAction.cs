namespace DmxConsole.Application.Commands.Selection;

/// <summary>
/// CLEAR (docs/COMMAND_SURFACE_KEY_SPEC.md §15, redefined again for the RELEASE-panel slice): the
/// ONE operator CLEAR semantic, converged onto by every UI entry point (CommandSurfaceViewModel.
/// PressClear, and every Razor Clear button that used to dispatch its own selection-clearing
/// command now calls that same method - see the ViewModel's own doc comment for the full list of
/// what one CLEAR press does).
///
/// Deliberately an <see cref="IConsoleAction"/> rather than an <see cref="IConsoleCommand"/>:
/// clearing the selection is an operator navigation gesture, so it must never enter Undo history
/// and must never clear the Redo stack (see IConsoleAction's own doc comment - that exclusion is
/// structural, enforced by CommandDispatcher.DispatchAction, not by convention).
///
/// Touches <see cref="ConsoleContext.Selection"/> and resets <see cref="ConsoleContext.SelectionCycle"/>
/// to a clean idle state (StartFreshOnNextSelection=false - "no completed programming action is
/// pending", matching an empty Selection) - both parts of the ONE Selection-related reset CLEAR
/// performs. It never touches Programmer/Editor values, never touches the shared
/// EditorContextStack. The command line's own composition/pending-digit reset and the RELEASE
/// softkey-context dismissal live in CommandSurfaceViewModel.PressClear (a UI-layer concern this
/// Action has no access to) - not duplicated here. It has no second-press/escalation behavior of
/// any kind: pressing CLEAR again simply clears an already-empty (and already-idle) selection
/// again. Backspace remains the sole editor of not-yet-resolved digits/tokens.
///
/// It is also deliberately EXCLUDED from Macro recording - see MacroRecorder.OnActionExecuted:
/// clearing the operating selection is a navigation gesture, never a step worth replaying.
/// </summary>
public sealed class ClearSelectionAction : IConsoleAction
{
    public CommandResult Execute(ConsoleContext context)
    {
        context.Selection.Clear();
        context.SelectionCycle.MarkSelectionStarted();
        return new CommandResult { ActionType = ConsoleActionType.ClearSelection };
    }
}
