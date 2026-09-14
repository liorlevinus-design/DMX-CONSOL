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

    /// <summary>What Undo-ing the current top of the stack would offer, without doing it -
    /// lets a caller decide whether to ask for confirmation before calling Undo.</summary>
    public UndoProposal? PeekUndo() => _undoStack.Count == 0
        ? null
        : (_undoStack.Peek() as IHasUndoRisk)?.PrepareUndo() ?? UndoProposal.SingleSafe(_undoStack.Peek().GetType().Name);

    /// <summary>Performs Undo. Omitting confirmedOptionId only ever auto-performs a Safe option -
    /// never a Destructive one. To proceed past a Destructive option, the caller must pass back
    /// the exact option Id from a prior PeekUndo()/Undo() call, proving it was shown to (and
    /// approved by) the operator/NL layer first. This gate lives here structurally - it is not a
    /// convention a caller has to remember to check.</summary>
    public UndoOutcome Undo(string? confirmedOptionId = null)
    {
        var proposal = PeekUndo();
        if (proposal is null) return new UndoOutcome(false, null);

        var chosen = confirmedOptionId is not null
            ? proposal.Options.FirstOrDefault(o => o.Id == confirmedOptionId)
            : proposal.Options.FirstOrDefault(o => o.Risk == UndoRisk.Safe);

        if (chosen is null || (chosen.Risk == UndoRisk.Destructive && confirmedOptionId != chosen.Id))
            return new UndoOutcome(false, proposal); // blocked - nothing popped, nothing changed

        var command = _undoStack.Pop();
        command.Undo(_context);
        _redoStack.Push(command);
        return new UndoOutcome(true, proposal);
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        var command = _redoStack.Pop();
        command.Execute(_context); // re-executing lets it recapture a fresh "previous state" for its next Undo
        _undoStack.Push(command);
    }
}
