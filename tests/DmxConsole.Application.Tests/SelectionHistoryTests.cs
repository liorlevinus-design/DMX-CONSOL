using DmxConsole.Application.Commands.Selection;
using DmxConsole.Application.CommandSurface;
using DmxConsole.Core;
using Xunit;

namespace DmxConsole.Application.Tests;

/// <summary>
/// Selection History rule: "Last Selection is always the most recent ordered Selection state
/// produced by any selection operation or transformation" (FIXTURE/GROUP/GUI/ODD/EVEN/REVERSE/any
/// future transform), a pure snapshot of Selection itself - never derived from or coupled to
/// Programmer contents - with CLEAR as the one deliberate exception (clears Current Selection,
/// never overwrites Last Selection). Implemented centrally in CommandDispatcher.Dispatch, driven
/// by SelectionCommandBase.ProducesSelectionSnapshot (see that type's own doc comment) - every
/// test here dispatches through the same CommandDispatcher any real caller (grammar, GUI, Group
/// panel) would use, never a second, parallel history mechanism.
/// </summary>
public class SelectionHistoryTests
{
    [Fact]
    public void FixtureRange_UpdatesLastSelection_InSelectionOrder()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 10);

        dispatcher.Dispatch(new SelectRangeCommand(1, 5));

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, context.SelectionCycle.LastSelection.Select(f => f.Number));
    }

    [Fact]
    public void GroupRecall_UpdatesLastSelection_ToTheGroupsMembership()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 10);
        dispatcher.Dispatch(new SelectRangeCommand(1, 4));
        var group = context.Groups.CreateFromSelection("Front", context.Selection, number: 4);
        dispatcher.Dispatch(new ClearSelectionCommand());

        dispatcher.Dispatch(new AddGroupToSelectionCommand(group));

        Assert.Equal(new[] { 1, 2, 3, 4 }, context.SelectionCycle.LastSelection.Select(f => f.Number));
    }

    [Fact]
    public void Odd_UpdatesLastSelection_ToTheFilteredSubset()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 10);
        dispatcher.Dispatch(new SelectRangeCommand(1, 10));

        dispatcher.Dispatch(new SelectOddCommand());

        Assert.Equal(new[] { 1, 3, 5, 7, 9 }, context.SelectionCycle.LastSelection.Select(f => f.Number));
    }

    [Fact]
    public void Even_UpdatesLastSelection_ToTheFilteredSubset()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 10);
        dispatcher.Dispatch(new SelectRangeCommand(1, 10));

        dispatcher.Dispatch(new SelectEvenCommand());

        Assert.Equal(new[] { 2, 4, 6, 8, 10 }, context.SelectionCycle.LastSelection.Select(f => f.Number));
    }

    [Fact]
    public void Reverse_UpdatesLastSelection_ToTheReversedOrder()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 5);
        dispatcher.Dispatch(new SelectRangeCommand(1, 5));

        dispatcher.Dispatch(new ReverseSelectionCommand());

        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, context.SelectionCycle.LastSelection.Select(f => f.Number));
    }

    [Fact]
    public void Clear_ClearsCurrentSelection_ButNeverOverwritesLastSelection()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 10);
        dispatcher.Dispatch(new SelectRangeCommand(1, 10));
        dispatcher.Dispatch(new SelectOddCommand());
        var expectedLast = context.SelectionCycle.LastSelection.Select(f => f.Number).ToList();

        // CLEAR-the-key is ClearSelectionAction, an IConsoleAction - dispatched via DispatchAction,
        // which never reaches CommandDispatcher.Dispatch (and therefore never even evaluates
        // ProducesSelectionSnapshot) - structurally, not just by convention.
        dispatcher.DispatchAction(new ClearSelectionAction());

        Assert.Empty(context.Selection.Items);
        Assert.Equal(expectedLast, context.SelectionCycle.LastSelection.Select(f => f.Number));
    }

    [Fact]
    public void BareClearSelectionCommand_AlsoNeverOverwritesLastSelection_EvenIfDispatchedDirectly()
    {
        // Belt-and-suspenders: even if some future caller dispatches ClearSelectionCommand (the
        // Command, not the Action) directly through Dispatch() instead of bundling it into a
        // composite (today's only real usage), ProducesSelectionSnapshot's own override must
        // still hold the line.
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 10);
        dispatcher.Dispatch(new SelectRangeCommand(1, 10));
        var expectedLast = context.SelectionCycle.LastSelection.Select(f => f.Number).ToList();

        dispatcher.Dispatch(new ClearSelectionCommand());

        Assert.Empty(context.Selection.Items);
        Assert.Equal(expectedLast, context.SelectionCycle.LastSelection.Select(f => f.Number));
    }

    /// <summary>The exact example from the Selection History rule's own report.</summary>
    [Fact]
    public void FixtureRecall_AfterOddThenClear_RecallsTheOddFilteredSet_NotTheOriginalRange()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 10);
        var composer = new CommandComposer(context);

        // FIXTURE 1 THRU 10 ENTER
        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.Number(10));
        dispatcher.Dispatch(composer.Push(CommandToken.Simple(CommandTokenKind.Enter)).ReadyOperation!);
        composer.Reset();

        // ODD
        dispatcher.Dispatch(new SelectOddCommand());
        Assert.Equal(new[] { 1, 3, 5, 7, 9 }, context.Selection.Items.Select(f => f.Number));

        // CLEAR (the key - IConsoleAction, never touches LastSelection)
        dispatcher.DispatchAction(new ClearSelectionAction());
        Assert.Empty(context.Selection.Items);
        Assert.Equal(new[] { 1, 3, 5, 7, 9 }, context.SelectionCycle.LastSelection.Select(f => f.Number));

        // FIXTURE . ENTER
        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Recall));
        final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));
        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);

        Assert.Equal(new[] { 1, 3, 5, 7, 9 }, context.Selection.Items.Select(f => f.Number));
    }

    /// <summary>The second example from the Selection History rule's own report: GROUP 4 -> EVEN
    /// -> CLEAR -> FIXTURE . must recall the EVEN result of Group 4, not the original Group 4
    /// membership.</summary>
    [Fact]
    public void FixtureRecall_AfterGroupRecallThenEvenThenClear_RecallsTheEvenFilteredGroupResult()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 10);
        dispatcher.Dispatch(new SelectRangeCommand(1, 8));
        var group4 = context.Groups.CreateFromSelection("Group4", context.Selection, number: 4);
        dispatcher.DispatchAction(new ClearSelectionAction());

        var composer = new CommandComposer(context);

        // GROUP 4 ENTER
        composer.Push(CommandToken.Simple(CommandTokenKind.Group));
        composer.Push(CommandToken.Number(4));
        dispatcher.Dispatch(composer.Push(CommandToken.Simple(CommandTokenKind.Enter)).ReadyOperation!);
        composer.Reset();
        Assert.Equal(group4.Fixtures.Select(f => f.Number), context.Selection.Items.Select(f => f.Number));

        // EVEN
        dispatcher.Dispatch(new SelectEvenCommand());
        var evenResult = context.Selection.Items.Select(f => f.Number).ToList();
        Assert.Equal(new[] { 2, 4, 6, 8 }, evenResult);

        // CLEAR
        dispatcher.DispatchAction(new ClearSelectionAction());
        Assert.Empty(context.Selection.Items);
        Assert.Equal(evenResult, context.SelectionCycle.LastSelection.Select(f => f.Number));

        // FIXTURE . ENTER
        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Simple(CommandTokenKind.Recall));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));
        Assert.True(final.IsComplete);
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.Equal(new[] { 2, 4, 6, 8 }, context.Selection.Items.Select(f => f.Number)); // NOT the original 1..8
    }

    [Fact]
    public void OrderedSelectionPreserved_NonMonotonicPickOrder_NeverResortedByFixtureId()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 40);
        int[] order = { 21, 4, 17, 8, 31 };

        foreach (var number in order)
            dispatcher.Dispatch(new AddFixtureToSelectionCommand(context.Patch.FindByNumber(number)!));

        Assert.Equal(order, context.SelectionCycle.LastSelection.Select(f => f.Number));
    }

    [Fact]
    public void Reverse_ThenRecall_PreservesTheReversedOrder_NotFixtureIdOrder()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 5);
        dispatcher.Dispatch(new SelectRangeCommand(1, 5));
        dispatcher.Dispatch(new ReverseSelectionCommand());
        dispatcher.DispatchAction(new ClearSelectionAction());

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Simple(CommandTokenKind.Recall));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, context.Selection.Items.Select(f => f.Number));
    }

    /// <summary>Selection != Programmer != Playback: a fixture with ZERO Programmer values must
    /// still appear in Last Selection just because it was selected, and a fixture that DOES have
    /// Programmer values but is no longer selected must never leak into Last Selection.</summary>
    [Fact]
    public void LastSelection_NeverDependsOnProgrammerContents()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 3);
        var f1 = context.Patch.FindByNumber(1)!;
        var f2 = context.Patch.FindByNumber(2)!;
        var f3 = context.Patch.FindByNumber(3)!;

        // Fixture 3 has a Programmer value but is NEVER selected.
        context.Programmer.SetChannel(f3.UniverseId, f3.AbsoluteIndex(f3.FindChannel(ChannelType.Dimmer)!), 200);

        // Fixture 1 and 2 are selected but have NO Programmer values at all.
        dispatcher.Dispatch(new AddFixtureToSelectionCommand(f1));
        dispatcher.Dispatch(new AddFixtureToSelectionCommand(f2));

        Assert.Equal(new[] { 1, 2 }, context.SelectionCycle.LastSelection.Select(f => f.Number));
        Assert.False(context.Programmer.HasStoredValue(f1.UniverseId, f1.AbsoluteIndex(f1.FindChannel(ChannelType.Dimmer)!), out _));
        Assert.False(context.Programmer.HasStoredValue(f2.UniverseId, f2.AbsoluteIndex(f2.FindChannel(ChannelType.Dimmer)!), out _));

        // Now deselect fixture 1 (still has no Programmer value) - Last Selection updates to
        // reflect ONLY the current Selection state, never influenced by fixture 3's unrelated
        // Programmer value.
        dispatcher.Dispatch(new RemoveFixtureFromSelectionCommand(f1));
        Assert.Equal(new[] { 2 }, context.SelectionCycle.LastSelection.Select(f => f.Number));
    }
}
