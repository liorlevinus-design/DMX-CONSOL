namespace DmxConsole.Application;

/// <summary>Standard undo/redo stacks over already-executed <see cref="IConsoleCommand"/>s.</summary>
public sealed class UndoRedoService
{
    private readonly ConsoleContext _context;
    private readonly Stack<IConsoleCommand> _undoStack = new();
    private readonly Stack<IConsoleCommand> _redoStack = new();

    public UndoRedoService(ConsoleContext context) => _context = context;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>Records a command that has already been executed successfully. Invalidates the redo history.</summary>
    public void Push(IConsoleCommand command)
    {
        _undoStack.Push(command);
        _redoStack.Clear();
    }

    public void Undo()
    {
        if (_undoStack.Count == 0) return;
        var command = _undoStack.Pop();
        command.Undo(_context);
        _redoStack.Push(command);
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        var command = _redoStack.Pop();
        command.Execute(_context); // re-executing lets it recapture a fresh "previous state" for its next Undo
        _undoStack.Push(command);
    }
}
