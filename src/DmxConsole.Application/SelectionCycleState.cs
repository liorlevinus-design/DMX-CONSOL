using DmxConsole.Core.Fixtures;

namespace DmxConsole.Application;

/// <summary>
/// Shared operator selection-cycle state. Selection remains visibly active after an execution
/// (for example AT 50), but the next new Fixture/Group selection starts a fresh cycle and
/// therefore replaces the old one. The gesture baseline stack lets a single CLEAR step back one
/// selection element/gesture (including an entire Group application), while CLEAR CLEAR can wipe
/// the whole current cycle. This state belongs to the Application layer rather than Razor.
/// </summary>
public sealed class SelectionCycleState
{
    private readonly List<IReadOnlyList<PatchedFixture>> _gestureBaselines = new();

    public bool StartFreshOnNextSelection { get; private set; }
    public bool HasGestureHistory => _gestureBaselines.Count > 0;

    /// <summary>Called after a non-selection execution consumed the current targets.</summary>
    public void MarkExecutionCompleted() => StartFreshOnNextSelection = true;

    /// <summary>Called after the first successful selection mutation of a new/current cycle.</summary>
    public void MarkSelectionStarted() => StartFreshOnNextSelection = false;

    /// <summary>Used when an external GUI selection has already established the current cycle.</summary>
    public void MarkSelectionSynchronized() => StartFreshOnNextSelection = false;

    /// <summary>
    /// Records the selection as it existed immediately before one operator selection gesture.
    /// If this gesture is the first one after an execution, older-cycle history is discarded.
    /// </summary>
    public void RecordGesture(IEnumerable<PatchedFixture> baseline, bool startsFreshCycle)
    {
        if (startsFreshCycle) _gestureBaselines.Clear();
        _gestureBaselines.Add(baseline.ToList());
        StartFreshOnNextSelection = false;
    }

    /// <summary>
    /// Pops the baseline for the most recent selection gesture. Restoring that snapshot is the
    /// semantic meaning of a single CLEAR: a whole Group/range gesture disappears as one unit,
    /// not merely its last fixture.
    /// </summary>
    public bool TryPopGesture(out IReadOnlyList<PatchedFixture> baseline)
    {
        if (_gestureBaselines.Count == 0)
        {
            baseline = Array.Empty<PatchedFixture>();
            return false;
        }

        int last = _gestureBaselines.Count - 1;
        baseline = _gestureBaselines[last];
        _gestureBaselines.RemoveAt(last);
        return true;
    }

    public void ClearGestureHistory() => _gestureBaselines.Clear();
}
