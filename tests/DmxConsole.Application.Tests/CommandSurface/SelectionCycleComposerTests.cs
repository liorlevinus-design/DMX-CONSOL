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
        Assert.Equal("1 THRU 3", final.PreviewText.ToUpperInvariant());
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
}
