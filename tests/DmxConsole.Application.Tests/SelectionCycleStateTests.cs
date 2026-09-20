namespace DmxConsole.Application.Tests;

/// <summary>Selection Cycle stabilization slice. SelectionCycleState no longer holds a
/// gesture-baseline stack (TryPopGesture/HasGestureHistory/ClearGestureHistory/the underlying
/// list are gone - CLEAR is a single, stateless, Selection-only action with no gesture history to
/// consult, per the CLEAR/Backspace stabilization slice). RecordGesture is now just the
/// "a selection gesture began" signal (item 3: the next Fixture/Group selection after a closed
/// cycle starts fresh).</summary>
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
    public void RecordGesture_ClearsStartFreshOnNextSelection()
    {
        var state = new SelectionCycleState();
        state.MarkExecutionCompleted();
        Assert.True(state.StartFreshOnNextSelection);

        state.RecordGesture();

        Assert.False(state.StartFreshOnNextSelection);
    }

    [Fact]
    public void MarkSelectionSynchronized_AlsoClearsStartFreshOnNextSelection()
    {
        var state = new SelectionCycleState();
        state.MarkExecutionCompleted();

        state.MarkSelectionSynchronized();

        Assert.False(state.StartFreshOnNextSelection);
    }

    [Fact]
    public void RecallState_IsPreservedAcrossExecutionCycles()
    {
        var (context, _, _) = TestFixtures.BuildConsole(fixtureCount: 3);
        var f1 = context.Patch.Fixtures.First(f => f.Number == 1);
        var state = context.SelectionCycle;

        state.RememberSelection(new[] { f1 });
        state.RememberGroup(7);
        state.RememberAt(42.5);
        state.MarkExecutionCompleted(); // a programming action happening must not disturb recall state

        Assert.Same(f1, Assert.Single(state.LastSelection));
        Assert.Equal(7, state.LastGroupNumber);
        Assert.Equal(42.5, state.LastAtPercent);
    }
}
