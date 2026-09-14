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
}
