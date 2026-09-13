using DmxConsole.Application.Commands.Groups;
using DmxConsole.Application.Commands.Selection;
using Xunit;

namespace DmxConsole.Application.Tests;

/// <summary>
/// Verifies the core promise behind the whole Command layer: several commands dispatched
/// together ("lower Cold Wash by 20 AND raise the Fronts by 10") behave as ONE transaction,
/// so a single Undo() reverts all of them - not just the last one.
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
}
