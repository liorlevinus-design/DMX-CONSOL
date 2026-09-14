using DmxConsole.Application.CommandSurface;
using DmxConsole.Core;
using DmxConsole.Core.Fixtures;

namespace DmxConsole.Application.Tests.CommandSurface;

public class CommandComposerTests
{
    private static byte DimmerValue(ConsoleContext context, int fixtureNumber)
    {
        var fixture = context.Patch.Fixtures.First(f => f.Number == fixtureNumber);
        var channel = fixture.FindChannel(ChannelType.Dimmer)!;
        context.Programmer.TryGetChannelValue(fixture.UniverseId, fixture.AbsoluteIndex(channel), out var value);
        return value;
    }

    [Fact]
    public void FixtureThruAt_ComposesDeterministically_AndDispatchesCorrectly()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 5);
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.Number(5));
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        var beforeEnter = composer.Push(CommandToken.Number(70));
        Assert.False(beforeEnter.IsComplete); // not finalized until Enter

        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        Assert.Equal("FIXTURE 1 THRU 5 AT 70", final.PreviewText.ToUpperInvariant());
        Assert.NotNull(final.ReadyOperation);

        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);

        Assert.Equal(5, context.Selection.Items.Count);
        for (int n = 1; n <= 5; n++)
            Assert.Equal((byte)Math.Round(70 / 100.0 * 255), DimmerValue(context, n));
    }

    [Fact]
    public void GroupAt_ResolvesGroupByNumber_AndAppliesToItsMembers()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 3);
        context.Selection.Add(context.Patch.Fixtures.First(f => f.Number == 2));
        var group = context.Groups.CreateFromSelection("Back Wash", context.Selection, number: 2);
        context.Selection.Clear();

        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Group));
        composer.Push(CommandToken.Number(2));
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        composer.Push(CommandToken.Number(40));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        var result = dispatcher.Dispatch(final.ReadyOperation!);
        Assert.True(result.Success);

        Assert.Contains(context.Selection.Items, f => f == group.Fixtures[0]);
        Assert.Equal((byte)Math.Round(40 / 100.0 * 255), DimmerValue(context, 2));
    }

    [Fact]
    public void PlusAndMinus_AdjustTheTargetSet()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 5);
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.Number(5));
        composer.Push(CommandToken.Simple(CommandTokenKind.Minus));
        composer.Push(CommandToken.Number(3));
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        composer.Push(CommandToken.Number(50));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.DoesNotContain(context.Selection.Items, f => f.Number == 3);
        Assert.Equal(4, context.Selection.Items.Count);
        Assert.Equal(0, DimmerValue(context, 3)); // never touched by At
        Assert.Equal((byte)Math.Round(50 / 100.0 * 255), DimmerValue(context, 1));
    }

    [Fact]
    public void Backspace_RemovesOnlyTheLastToken()
    {
        var (context, _, _) = TestFixtures.BuildConsole(fixtureCount: 5);
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        var afterBackspace = composer.Push(CommandToken.Simple(CommandTokenKind.Backspace));

        Assert.Equal(2, afterBackspace.Tokens.Count); // Fixture, 1 remain - Thru removed
        Assert.Equal("FIXTURE 1", afterBackspace.PreviewText.ToUpperInvariant());
    }

    [Fact]
    public void Clear_OnNonEmptyLine_ClearsComposition_WithoutTouchingSelection()
    {
        var (context, _, _) = TestFixtures.BuildConsole(fixtureCount: 5);
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Number(1));
        var afterClear = composer.Push(CommandToken.Simple(CommandTokenKind.Clear));

        Assert.Empty(afterClear.Tokens);
        Assert.False(afterClear.EmptyClearRequested);
    }

    [Fact]
    public void Clear_OnAlreadyEmptyLine_SignalsEmptyClearRequested_AndComposerNeverTouchesContext()
    {
        var (context, _, _) = TestFixtures.BuildConsole(fixtureCount: 5);
        var composer = new CommandComposer(context);

        var result = composer.Push(CommandToken.Simple(CommandTokenKind.Clear));

        Assert.True(result.EmptyClearRequested);
        Assert.Empty(context.Selection.Items); // composer never mutated ConsoleContext itself
    }

    [Fact]
    public void Enter_OnIncompleteCommand_PreservesTokens_AndDoesNotGuess()
    {
        var (context, _, _) = TestFixtures.BuildConsole(fixtureCount: 5);
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.False(final.IsComplete);
        Assert.Null(final.ReadyOperation);
        Assert.NotEmpty(final.Tokens); // command line is preserved, not cleared
        Assert.Contains(CommandTokenKind.Number, final.ExpectedNext);
    }

    [Fact]
    public void Enter_OnUnresolvedFixtureNumber_ReturnsError_AndPreservesTokens()
    {
        var (context, _, _) = TestFixtures.BuildConsole(fixtureCount: 2);
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Number(99));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.False(final.IsComplete);
        Assert.Null(final.ReadyOperation);
        Assert.NotNull(final.Error);
        Assert.NotEmpty(final.Tokens);
    }

    [Fact]
    public void FixtureSelectionOnly_NoAt_StillComposesAndDispatches()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 3);
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Number(2));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.Single(context.Selection.Items);
        Assert.Equal(2, context.Selection.Items[0].Number);
    }

    [Fact]
    public void ComposedOperation_UndoesThroughExistingUndoRedoService()
    {
        var (context, dispatcher, undoRedo) = TestFixtures.BuildConsole(fixtureCount: 5);
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.Number(3));
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        composer.Push(CommandToken.Number(80));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        dispatcher.Dispatch(final.ReadyOperation!);
        Assert.Equal(3, context.Selection.Items.Count);

        var outcome = undoRedo.Undo();
        Assert.True(outcome.Performed);
        Assert.Empty(context.Selection.Items);
        Assert.Equal(0, DimmerValue(context, 1));
    }
}
