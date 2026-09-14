using DmxConsole.Application.Commands.Executors;
using DmxConsole.Core.Engine;
using Xunit;

namespace DmxConsole.Application.Tests;

public class ExecutorCommandTests
{
    [Fact]
    public void CreateExecutorCommand_CreatesExecutor_UndoRequiresConfirmation_ThenRemoves()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(1);

        var result = dispatcher.Dispatch(new CreateExecutorCommand());

        Assert.True(result.Success);
        Assert.NotNull(result.Executor);
        Assert.Single(context.Executors.Executors);

        var blocked = undoRedo.Undo();
        Assert.False(blocked.Performed);
        Assert.Single(context.Executors.Executors);

        var proposal = undoRedo.PeekUndo()!;
        var confirmed = undoRedo.Undo(proposal.Options.Single(o => o.Risk == UndoRisk.Destructive).Id);
        Assert.True(confirmed.Performed);
        Assert.Empty(context.Executors.Executors);
    }

    [Fact]
    public void RemoveExecutorCommand_RemovesAndUndoReinserts_Safely()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(1);
        var executor = context.Executors.Add(); // seeded directly - this test is only about Remove

        var result = dispatcher.Dispatch(new RemoveExecutorCommand(executor));

        Assert.True(result.Success);
        Assert.Empty(context.Executors.Executors);

        var outcome = undoRedo.Undo(); // Safe - no confirmation needed
        Assert.True(outcome.Performed);
        Assert.Contains(executor, context.Executors.Executors);
    }

    [Fact]
    public void RemoveExecutorCommand_FailsGracefully_WhenAlreadyRemoved()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(1);
        var executor = new Executor(1);

        var result = dispatcher.Dispatch(new RemoveExecutorCommand(executor));

        Assert.False(result.Success);
    }

    [Fact]
    public void AssignExecutorCommand_AssignsAndUndoRestoresPreviousSource_IncludingEmpty()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(1);
        var executor = new Executor(1);
        var cueList = new CueList();

        var result = dispatcher.Dispatch(new AssignExecutorCommand(executor, cueList));

        Assert.True(result.Success);
        Assert.Same(cueList, executor.Source);

        var outcome = undoRedo.Undo();
        Assert.True(outcome.Performed);
        Assert.Null(executor.Source); // was empty before - Undo re-empties it
    }

    [Fact]
    public void SetExecutorLevelCommand_SetsAndUndoRestoresPreviousLevel()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(1);
        var executor = new Executor(1) { FaderLevel = 1.0 };

        dispatcher.Dispatch(new SetExecutorLevelCommand(executor, 0.5));
        Assert.Equal(0.5, executor.FaderLevel);

        var outcome = undoRedo.Undo();
        Assert.True(outcome.Performed);
        Assert.Equal(1.0, executor.FaderLevel);
    }
}
