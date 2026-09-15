using DmxConsole.Application.Commands.Groups;
using DmxConsole.Application.Commands.Selection;
using Xunit;

namespace DmxConsole.Application.Tests;

public class GroupCommandTests
{
    [Fact]
    public void CreateGroupCommand_SavesCurrentSelection_AndUndoRemovesIt()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(5);
        dispatcher.Dispatch(new SelectRangeCommand(1, 3));

        var result = dispatcher.Dispatch(new CreateGroupCommand("Back Wash"));

        Assert.True(result.Success);
        Assert.Equal(ConsoleActionType.CreateGroup, result.ActionType);
        Assert.NotNull(result.Group);
        Assert.Equal("Back Wash", result.Group!.Name);
        Assert.Equal(new[] { 1, 2, 3 }, result.Group.Fixtures.Select(f => f.Number));
        Assert.Single(context.Groups.Groups);

        // Undo-ing a freshly Created Group is Destructive (it deletes a persistent show object) -
        // requires the confirmed option id (Step F's UndoRisk gate), not a bare Undo().
        var proposal = undoRedo.PeekUndo()!;
        undoRedo.Undo(proposal.Options.Single(o => o.Risk == UndoRisk.Destructive).Id);
        Assert.Empty(context.Groups.Groups);
    }

    [Fact]
    public void RemoveGroupCommand_RemovesGroup_AndUndoReinsertsAtOriginalPosition()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(5);
        dispatcher.Dispatch(new SelectRangeCommand(1, 1));
        var groupA = dispatcher.Dispatch(new CreateGroupCommand("A")).Group!;
        dispatcher.Dispatch(new SelectRangeCommand(2, 2));
        var groupB = dispatcher.Dispatch(new CreateGroupCommand("B")).Group!;

        var result = dispatcher.Dispatch(new RemoveGroupCommand(groupA));

        Assert.True(result.Success);
        Assert.DoesNotContain(groupA, context.Groups.Groups);
        Assert.Single(context.Groups.Groups);

        undoRedo.Undo();
        Assert.Equal(new[] { groupA, groupB }, context.Groups.Groups);
    }

    [Fact]
    public void RemoveGroupCommand_FailsGracefully_WhenGroupNotPresent()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(3);
        dispatcher.Dispatch(new SelectRangeCommand(1, 1));
        var group = dispatcher.Dispatch(new CreateGroupCommand("Solo")).Group!;
        dispatcher.Dispatch(new RemoveGroupCommand(group)); // already removed once

        var result = dispatcher.Dispatch(new RemoveGroupCommand(group));

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void StoreGroupCommand_CreatesGroupAtExplicitNumber_AndUndoRemovesIt()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(5);
        dispatcher.Dispatch(new SelectRangeCommand(1, 2));

        var result = dispatcher.Dispatch(new StoreGroupCommand("Front Wash", number: 7));

        Assert.True(result.Success);
        Assert.Equal(7, result.Group!.Number);
        Assert.Equal(new[] { 1, 2 }, result.Group.Fixtures.Select(f => f.Number));

        var proposal = undoRedo.PeekUndo()!;
        undoRedo.Undo(proposal.Options.Single(o => o.Risk == UndoRisk.Destructive).Id);
        Assert.Empty(context.Groups.Groups);
    }

    [Fact]
    public void StoreGroupCommand_RejectsExplicitNumberAlreadyInUse()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(5);
        dispatcher.Dispatch(new SelectRangeCommand(1, 1));
        dispatcher.Dispatch(new StoreGroupCommand("A", number: 3));
        dispatcher.Dispatch(new SelectRangeCommand(2, 2));

        var result = dispatcher.Dispatch(new StoreGroupCommand("B", number: 3));

        Assert.False(result.Success);
        Assert.Contains("already in use", result.Error);
        Assert.Single(context.Groups.Groups); // second store never happened
    }

    [Fact]
    public void StoreGroupCommand_OnExistingGroup_ReplacesMembership_AndUndoRestoresIt()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(5);
        dispatcher.Dispatch(new SelectRangeCommand(1, 2));
        var group = dispatcher.Dispatch(new StoreGroupCommand("Wash", number: 1)).Group!;

        dispatcher.Dispatch(new ClearSelectionCommand());
        dispatcher.Dispatch(new SelectRangeCommand(3, 5));
        var result = dispatcher.Dispatch(new StoreGroupCommand("Wash", number: 1, existingGroup: group));

        Assert.True(result.Success);
        Assert.Equal(new[] { 3, 4, 5 }, group.Fixtures.Select(f => f.Number));

        undoRedo.Undo();
        Assert.Equal(new[] { 1, 2 }, group.Fixtures.Select(f => f.Number));
    }

    [Fact]
    public void RenameGroupCommand_RenamesGroup_AndUndoRestoresPreviousName()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(3);
        dispatcher.Dispatch(new SelectRangeCommand(1, 1));
        var group = dispatcher.Dispatch(new StoreGroupCommand("Old Name", number: 1)).Group!;

        var result = dispatcher.Dispatch(new RenameGroupCommand(group, "New Name"));

        Assert.True(result.Success);
        Assert.Equal("New Name", group.Name);

        undoRedo.Undo();
        Assert.Equal("Old Name", group.Name);
    }
}
