using DmxConsole.Core.Engine;

namespace DmxConsole.Application.Commands.Executors;

/// <summary>Creates a new (empty, unassigned) Executor. Mirrors CreateGroupCommand's pattern -
/// Undo-ing this deletes the freshly created Executor, a persistent show object, so it needs
/// explicit destructive confirmation (IHasUndoRisk).</summary>
public sealed class CreateExecutorCommand : IConsoleCommand, IHasUndoRisk
{
    private readonly int? _number;
    private Executor? _created;

    public CreateExecutorCommand(int? number = null) => _number = number;

    public CommandResult Execute(ConsoleContext context)
    {
        _created = context.Executors.Add(_number);
        return new CommandResult { ActionType = ConsoleActionType.CreateExecutor, Executor = _created };
    }

    public void Undo(ConsoleContext context)
    {
        if (_created is not null) context.Executors.Remove(_created);
    }

    public UndoProposal PrepareUndo()
    {
        var description = $"Delete Executor {_created?.Number}";
        return new UndoProposal(description, new[] { new UndoOption("delete", description, UndoRisk.Destructive) });
    }
}
