namespace DmxConsole.Application;

/// <summary>
/// An operational/runtime action - Go/Back/Stop/Pause/Resume/Flash Press/Flash Release. Goes
/// through the same Application layer as every <see cref="IConsoleCommand"/> (no UI-to-Core
/// bypass, ever) and returns the same structured CommandResult - but it is NOT editing history:
/// dispatching one never touches UndoRedoService at all, so it can never be undone and can never
/// clear the redo stack. See CommandDispatcher.DispatchAction.
/// </summary>
public interface IConsoleAction
{
    CommandResult Execute(ConsoleContext context);
}
