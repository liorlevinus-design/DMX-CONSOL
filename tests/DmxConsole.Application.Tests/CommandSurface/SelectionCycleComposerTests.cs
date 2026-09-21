using DmxConsole.Application.CommandSurface;

namespace DmxConsole.Application.Tests.CommandSurface;

public class SelectionCycleComposerTests
{
    [Fact]
    public void FixtureRecall_RestoresLastSelection()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 3);
        context.SelectionCycle.RememberSelection(context.Patch.Fixtures.Where(f => f.Number is 1 or 3));
        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        composer.Push(CommandToken.Simple(CommandTokenKind.Recall));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));
        Assert.True(final.IsComplete);
        dispatcher.Dispatch(final.ReadyOperation!);
        Assert.Equal(new[] { 1, 3 }, context.Selection.Items.Select(f => f.Number).ToArray());
    }

    [Fact]
    public void GroupRecall_UsesLastGroupNumber()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 3);
        context.Selection.Add(context.Patch.Fixtures.First(f => f.Number == 2));
        context.Groups.CreateFromSelection("Back", context.Selection, number: 7);
        context.Selection.Clear();
        context.SelectionCycle.RememberGroup(7);
        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.Group));
        composer.Push(CommandToken.Simple(CommandTokenKind.Recall));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));
        dispatcher.Dispatch(final.ReadyOperation!);
        Assert.Equal(2, Assert.Single(context.Selection.Items).Number);
    }

    [Fact]
    public void AtRecall_AppliesLastLevelToCurrentSelection()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 1);
        var fixture = Assert.Single(context.Patch.Fixtures);
        context.Selection.Add(fixture);
        context.SelectionCycle.RememberAt(42);
        var composer = new CommandComposer(context);
        composer.Push(CommandToken.Simple(CommandTokenKind.At));
        composer.Push(CommandToken.Simple(CommandTokenKind.Recall));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));
        Assert.True(dispatcher.Dispatch(final.ReadyOperation!).Success);
        var channel = fixture.FindChannel(DmxConsole.Core.ChannelType.Dimmer)!;
        Assert.True(context.Programmer.TryGetChannelValue(fixture.UniverseId, fixture.AbsoluteIndex(channel), out var value));
        Assert.Equal((byte)Math.Round(.42 * 255), value);
    }

    [Fact]
    public void BareNumbers_DefaultToFixtureContext()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 5);
        var composer = new CommandComposer(context);

        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        composer.Push(CommandToken.Number(3));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        // Quick-Patch/implicit-Fixture convergence slice (§4): "1 THRU 3" now shows the same
        // "FIXTURE 1 THRU 3" preview explicit entry would - never a silently-different display.
        Assert.Equal("FIXTURE 1 THRU 3", final.PreviewText.ToUpperInvariant());
        Assert.NotNull(final.ReadyOperation);

        dispatcher.Dispatch(final.ReadyOperation!);
        Assert.Equal(new[] { 1, 2, 3 }, context.Selection.Items.Select(f => f.Number).ToArray());
    }

    [Fact]
    public void AppendMode_AddsWithoutTogglingExistingSelectionOff()
    {
        var (context, dispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 3);
        context.Selection.Add(context.Patch.Fixtures.First(f => f.Number == 1));

        var composer = new CommandComposer(context) { ReplaceSelectionOnResolve = false };
        composer.Push(CommandToken.Number(1));
        composer.Push(CommandToken.Number(2));
        var final = composer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        Assert.True(final.IsComplete);
        dispatcher.Dispatch(final.ReadyOperation!);

        Assert.Equal(new[] { 1, 2 }, context.Selection.Items.Select(f => f.Number).ToArray());
    }

    [Fact]
    public void At_ClosesSelectionCycle_SelectionOnlyDoesNot()
    {
        var (context, _, _) = TestFixtures.BuildConsole(fixtureCount: 2);

        var selectionOnly = new CommandComposer(context);
        selectionOnly.Push(CommandToken.Number(1));
        var selected = selectionOnly.Push(CommandToken.Simple(CommandTokenKind.Enter));
        Assert.False(selected.EndsSelectionCycle);

        var withAt = new CommandComposer(context);
        withAt.Push(CommandToken.Number(1));
        withAt.Push(CommandToken.Simple(CommandTokenKind.At));
        withAt.Push(CommandToken.Number(50));
        var executed = withAt.Push(CommandToken.Simple(CommandTokenKind.Enter));
        Assert.True(executed.EndsSelectionCycle);
    }

    /// <summary>Quick-Patch/implicit-Fixture convergence slice (§4): explicit "FIXTURE 1 THRU 8"
    /// and implicit "1 THRU 8" must produce composition results that are indistinguishable in
    /// every way that matters - same Tokens shape, same PreviewText, same completion, same
    /// EndsSelectionCycle, same dispatched Selection outcome. Never a second, parallel "numbers
    /// without object" engine - the implicit form runs through the identical CommandComposer.Build
    /// path once Push() has injected the Fixture token.</summary>
    [Fact]
    public void ImplicitFixture_And_ExplicitFixture_ProduceEquivalentCompositions_AndIdenticalDispatch()
    {
        var (explicitContext, explicitDispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 8);
        var explicitComposer = new CommandComposer(explicitContext);
        explicitComposer.Push(CommandToken.Simple(CommandTokenKind.Fixture));
        explicitComposer.Push(CommandToken.Number(1));
        explicitComposer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        explicitComposer.Push(CommandToken.Number(8));
        var explicitFinal = explicitComposer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        var (implicitContext, implicitDispatcher, _) = TestFixtures.BuildConsole(fixtureCount: 8);
        var implicitComposer = new CommandComposer(implicitContext);
        implicitComposer.Push(CommandToken.Number(1));
        implicitComposer.Push(CommandToken.Simple(CommandTokenKind.Thru));
        implicitComposer.Push(CommandToken.Number(8));
        var implicitFinal = implicitComposer.Push(CommandToken.Simple(CommandTokenKind.Enter));

        // Same shape: token kinds (Fixture, Number, Thru, Number), same preview text.
        Assert.Equal(explicitFinal.Tokens.Select(t => t.Kind), implicitFinal.Tokens.Select(t => t.Kind));
        Assert.Equal(explicitFinal.PreviewText, implicitFinal.PreviewText);
        Assert.Equal(explicitFinal.IsComplete, implicitFinal.IsComplete);
        Assert.Equal(explicitFinal.EndsSelectionCycle, implicitFinal.EndsSelectionCycle);

        explicitDispatcher.Dispatch(explicitFinal.ReadyOperation!);
        implicitDispatcher.Dispatch(implicitFinal.ReadyOperation!);

        Assert.Equal(
            explicitContext.Selection.Items.Select(f => f.Number),
            implicitContext.Selection.Items.Select(f => f.Number));
        Assert.Equal(explicitContext.SelectionCycle.StartFreshOnNextSelection, implicitContext.SelectionCycle.StartFreshOnNextSelection);
    }
}
