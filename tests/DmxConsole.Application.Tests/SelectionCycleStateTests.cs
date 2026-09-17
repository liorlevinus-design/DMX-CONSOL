namespace DmxConsole.Application.Tests;

public class SelectionCycleStateTests
{
    [Fact]
    public void ExecutionMarksNextSelectionFresh()
    {
        var state = new SelectionCycleState();

        Assert.False(state.StartFreshOnNextSelection);
        state.MarkExecutionCompleted();
        Assert.True(state.StartFreshOnNextSelection);
        state.MarkSelectionStarted();
        Assert.False(state.StartFreshOnNextSelection);
    }

    [Fact]
    public void StartingFreshCycleDropsOlderGestureHistory()
    {
        var (context, _, _) = TestFixtures.BuildConsole(fixtureCount: 3);
        var f1 = context.Patch.Fixtures.First(f => f.Number == 1);
        var f2 = context.Patch.Fixtures.First(f => f.Number == 2);
        var state = context.SelectionCycle;

        state.RecordGesture(Array.Empty<DmxConsole.Core.Fixtures.PatchedFixture>(), startsFreshCycle: false);
        state.RecordGesture(new[] { f1 }, startsFreshCycle: true);

        Assert.True(state.TryPopGesture(out var baseline));
        Assert.Single(baseline);
        Assert.Same(f1, baseline[0]);
        Assert.False(state.TryPopGesture(out _));
    }

    [Fact]
    public void GestureBaselineCanRepresentWholeGroupStep()
    {
        var (context, _, _) = TestFixtures.BuildConsole(fixtureCount: 3);
        var beforeGroup = new[] { context.Patch.Fixtures.First(f => f.Number == 1) };
        var state = context.SelectionCycle;

        state.RecordGesture(beforeGroup, startsFreshCycle: false);

        Assert.True(state.TryPopGesture(out var baseline));
        Assert.Equal(new[] { 1 }, baseline.Select(f => f.Number).ToArray());
    }
}
