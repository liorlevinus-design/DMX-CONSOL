using DmxConsole.Application.Commands.Groups;
using DmxConsole.Application.Commands.Selection;
using Xunit;

namespace DmxConsole.Application.Tests;

public class SelectionCommandTests
{
    [Fact]
    public void ToggleFixtureCommand_AddsThenUndoRemoves()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(3);
        var fixture = context.Patch.FindByNumber(1)!;

        var result = dispatcher.Dispatch(new ToggleFixtureCommand(fixture));

        Assert.True(result.Success);
        Assert.Equal(ConsoleActionType.ToggleFixture, result.ActionType);
        Assert.Contains(fixture, result.AffectedFixtures);
        Assert.True(context.Selection.Contains(fixture));

        undoRedo.Undo();
        Assert.False(context.Selection.Contains(fixture));
    }

    [Fact]
    public void SelectRangeCommand_SelectsInclusiveRange_AndUndoRestoresPrior()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(10);
        dispatcher.Dispatch(new ToggleFixtureCommand(context.Patch.FindByNumber(1)!));

        dispatcher.Dispatch(new SelectRangeCommand(5, 7));

        Assert.Equal(new[] { 1, 5, 6, 7 }, context.Selection.Items.Select(f => f.Number));

        undoRedo.Undo();
        Assert.Equal(new[] { 1 }, context.Selection.Items.Select(f => f.Number));
    }

    [Fact]
    public void SelectOddCommand_KeepsOddPositions_AndUndoRestores()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(6);
        dispatcher.Dispatch(new SelectRangeCommand(1, 6));

        dispatcher.Dispatch(new SelectOddCommand());
        Assert.Equal(new[] { 1, 3, 5 }, context.Selection.Items.Select(f => f.Number));

        undoRedo.Undo();
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, context.Selection.Items.Select(f => f.Number));
    }

    [Fact]
    public void SelectEvenCommand_KeepsEvenPositions()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(6);
        dispatcher.Dispatch(new SelectRangeCommand(1, 6));

        dispatcher.Dispatch(new SelectEvenCommand());
        Assert.Equal(new[] { 2, 4, 6 }, context.Selection.Items.Select(f => f.Number));
    }

    [Fact]
    public void ClearSelectionCommand_EmptiesSelection_AndUndoRestores()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(3);
        dispatcher.Dispatch(new SelectRangeCommand(1, 3));

        dispatcher.Dispatch(new ClearSelectionCommand());
        Assert.Empty(context.Selection.Items);

        undoRedo.Undo();
        Assert.Equal(new[] { 1, 2, 3 }, context.Selection.Items.Select(f => f.Number));
    }

    [Fact]
    public void NextFixtureCommand_AdvancesSelection_AndUndoRestores()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(3);
        dispatcher.Dispatch(new ToggleFixtureCommand(context.Patch.FindByNumber(1)!));

        dispatcher.Dispatch(new NextFixtureCommand());
        Assert.Equal(new[] { 2 }, context.Selection.Items.Select(f => f.Number));

        undoRedo.Undo();
        Assert.Equal(new[] { 1 }, context.Selection.Items.Select(f => f.Number));
    }

    [Fact]
    public void PreviousFixtureCommand_WrapsAtStart()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(3);
        dispatcher.Dispatch(new ToggleFixtureCommand(context.Patch.FindByNumber(1)!));

        dispatcher.Dispatch(new PreviousFixtureCommand());
        Assert.Equal(new[] { 3 }, context.Selection.Items.Select(f => f.Number));
    }

    [Fact]
    public void AddGroupToSelectionCommand_AddsMembers_AndUndoRestores()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(5);
        dispatcher.Dispatch(new SelectRangeCommand(1, 2));
        var group = dispatcher.Dispatch(new CreateGroupCommand("Test Group")).Group!;
        dispatcher.Dispatch(new ClearSelectionCommand());

        dispatcher.Dispatch(new AddGroupToSelectionCommand(group));
        Assert.Equal(new[] { 1, 2 }, context.Selection.Items.Select(f => f.Number));

        undoRedo.Undo();
        Assert.Empty(context.Selection.Items);
    }

    [Fact]
    public void Redo_AfterUndo_ReappliesTheChange()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(3);
        dispatcher.Dispatch(new SelectRangeCommand(1, 2));

        undoRedo.Undo();
        Assert.Empty(context.Selection.Items);

        undoRedo.Redo();
        Assert.Equal(new[] { 1, 2 }, context.Selection.Items.Select(f => f.Number));
    }

    [Fact]
    public void Push_AfterUndo_ClearsRedoStack()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(5);
        dispatcher.Dispatch(new SelectRangeCommand(1, 2));
        undoRedo.Undo();
        Assert.True(undoRedo.CanRedo);

        dispatcher.Dispatch(new ToggleFixtureCommand(context.Patch.FindByNumber(5)!));

        Assert.False(undoRedo.CanRedo);
    }
}
