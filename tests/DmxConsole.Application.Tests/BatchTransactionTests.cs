using DmxConsole.Application.Commands.Groups;
using DmxConsole.Application.Commands.Selection;
using Xunit;

namespace DmxConsole.Application.Tests;

/// <summary>
/// Verifies the core promise behind the whole Command layer: several commands dispatched
/// together ("lower Cold Wash by 20 AND raise the Fronts by 10") behave as ONE atomic
/// transaction - every child result is preserved (not just the last one), a mid-batch
/// failure rolls back everything already applied and never reaches the undo stack, and a
/// single Undo()/Redo() reverts/reapplies the whole thing.
/// </summary>
public class BatchTransactionTests
{
    [Fact]
    public void DispatchBatch_AppliesAllCommands()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(6);

        dispatcher.DispatchBatch(new IConsoleCommand[]
        {
            new SelectRangeCommand(1, 2),
            new SelectRangeCommand(5, 6),
        });

        Assert.Equal(new[] { 1, 2, 5, 6 }, context.Selection.Items.Select(f => f.Number));
    }

    [Fact]
    public void DispatchBatch_Success_ReturnsChildResultForEveryCommand()
    {
        var (_, dispatcher, _) = TestFixtures.BuildConsole(6);

        var result = dispatcher.DispatchBatch(new IConsoleCommand[]
        {
            new SelectRangeCommand(1, 2),
            new SelectRangeCommand(5, 6),
        });

        Assert.True(result.Success);
        Assert.Equal(ConsoleActionType.Batch, result.ActionType);
        Assert.Equal(2, result.ChildResults.Count);
        Assert.Equal(ConsoleActionType.SelectRange, result.ChildResults[0].ActionType);
        Assert.Equal(ConsoleActionType.SelectRange, result.ChildResults[1].ActionType);
        Assert.All(result.ChildResults, r => Assert.True(r.Success));
    }

    [Fact]
    public void DispatchBatch_SingleUndo_RevertsAllCommandsInReverseOrder()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(6);
        dispatcher.Dispatch(new ToggleFixtureCommand(context.Patch.FindByNumber(3)!));

        dispatcher.DispatchBatch(new IConsoleCommand[]
        {
            new SelectRangeCommand(1, 2),
            new SelectRangeCommand(5, 6),
        });
        Assert.Equal(new[] { 3, 1, 2, 5, 6 }, context.Selection.Items.Select(f => f.Number));

        undoRedo.Undo(); // one Undo() call

        Assert.Equal(new[] { 3 }, context.Selection.Items.Select(f => f.Number));
    }

    [Fact]
    public void DispatchBatch_MixingSelectionAndGroupCommands_UndoesBothAsOneTransaction()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(5);
        dispatcher.Dispatch(new SelectRangeCommand(4, 4));
        var group = dispatcher.Dispatch(new CreateGroupCommand("Fronts")).Group!;
        dispatcher.Dispatch(new ClearSelectionCommand());

        dispatcher.DispatchBatch(new IConsoleCommand[]
        {
            new AddGroupToSelectionCommand(group),
            new SelectRangeCommand(1, 1),
        });
        Assert.Equal(new[] { 4, 1 }, context.Selection.Items.Select(f => f.Number));

        undoRedo.Undo();

        Assert.Empty(context.Selection.Items);
    }

    [Fact]
    public void DispatchBatch_Redo_ReappliesTheWholeTransaction()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(6);

        dispatcher.DispatchBatch(new IConsoleCommand[]
        {
            new SelectRangeCommand(1, 2),
            new SelectRangeCommand(5, 6),
        });
        undoRedo.Undo();
        Assert.Empty(context.Selection.Items);

        undoRedo.Redo();

        Assert.Equal(new[] { 1, 2, 5, 6 }, context.Selection.Items.Select(f => f.Number));
    }

    [Fact]
    public void DispatchBatch_SecondCommandFails_FirstIsAutomaticallyRolledBack()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(5);
        var missingGroup = dispatcher.Dispatch(new CreateGroupCommand("Temp")).Group!;
        dispatcher.Dispatch(new RemoveGroupCommand(missingGroup)); // group no longer exists from here on

        var result = dispatcher.DispatchBatch(new IConsoleCommand[]
        {
            new SelectRangeCommand(1, 2),          // succeeds
            new RemoveGroupCommand(missingGroup),  // fails: already removed
        });

        Assert.False(result.Success);
        Assert.Equal(2, result.ChildResults.Count);
        Assert.True(result.ChildResults[0].Success);
        Assert.False(result.ChildResults[1].Success);

        // The successful first command must have been rolled back - selection is untouched.
        Assert.Empty(context.Selection.Items);
    }

    [Fact]
    public void DispatchBatch_ThatFails_NeverReachesTheUndoStack()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(5);
        var missingGroup = dispatcher.Dispatch(new CreateGroupCommand("Temp")).Group!;
        dispatcher.Dispatch(new RemoveGroupCommand(missingGroup));
        dispatcher.Dispatch(new ToggleFixtureCommand(context.Patch.FindByNumber(2)!)); // known-good undo target

        dispatcher.DispatchBatch(new IConsoleCommand[]
        {
            new SelectRangeCommand(1, 1),
            new RemoveGroupCommand(missingGroup), // fails
        });

        // Undo must revert the ToggleFixtureCommand above, NOT anything from the failed batch,
        // proving the failed batch was never pushed onto the undo stack.
        undoRedo.Undo();
        Assert.Empty(context.Selection.Items);
    }
}
