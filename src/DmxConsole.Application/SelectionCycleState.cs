using DmxConsole.Core.Fixtures;

namespace DmxConsole.Application;

/// <summary>
/// Shared operator selection-cycle state (docs/COMMAND_SURFACE_KEY_SPEC.md - Selection Cycle
/// stabilization slice). Selection remains visibly active after a programming action (for
/// example AT 50, HOME, RELEASE, CAPTURE ALL, an Encoder commit), but the next new Fixture/Group
/// selection starts a fresh cycle and therefore replaces the old one rather than accumulating
/// onto it. This state belongs to the Application layer rather than Razor.
///
/// The gesture-baseline undo-step stack (RecordGesture/TryPopGesture/ClearGestureHistory/
/// HasGestureHistory) that used to back a "CLEAR steps back one gesture, CLEAR CLEAR wipes
/// everything" design has been removed - CLEAR is now a single, stateless, Selection-only action
/// (ClearSelectionAction) with no escalation and no gesture history to consult. RecordGesture is
/// kept only as the "the current selection gesture has begun" signal, matching item 3's "the next
/// Fixture/Group selection after a closed cycle starts a fresh Selection" - not as a baseline
/// stack to pop.
/// </summary>
public sealed class SelectionCycleState
{
    private IReadOnlyList<PatchedFixture> _lastSelection = Array.Empty<PatchedFixture>();

    public bool StartFreshOnNextSelection { get; private set; }
    public IReadOnlyList<PatchedFixture> LastSelection => _lastSelection;
    public int? LastGroupNumber { get; private set; }
    public double? LastAtPercent { get; private set; }

    public void RememberSelection(IEnumerable<PatchedFixture> fixtures) => _lastSelection = fixtures.Distinct().ToList();
    public void RememberGroup(int number) => LastGroupNumber = number;
    public void RememberAt(double percent) => LastAtPercent = percent;

    /// <summary>Called after a programming action (AT with value, FULL, HOME, family PRESET
    /// recall, family/parameter RELEASE, CAPTURE ALL, an Encoder commit) consumed the current
    /// Selection as its targets - the Selection stays visibly selected, but the NEXT Fixture/Group
    /// selection should replace it rather than accumulate onto it.</summary>
    public void MarkExecutionCompleted() => StartFreshOnNextSelection = true;

    /// <summary>Called after the first successful selection mutation of a new/current cycle.</summary>
    public void MarkSelectionStarted() => StartFreshOnNextSelection = false;

    /// <summary>Used when an external GUI selection has already established the current cycle.</summary>
    public void MarkSelectionSynchronized() => StartFreshOnNextSelection = false;

    /// <summary>Marks that a selection gesture has begun (the cycle is now open) - the counterpart
    /// to <see cref="MarkExecutionCompleted"/>. Every caller that mutates Selection on a successful
    /// dispatch calls this so the cycle stays open for the operator's next accumulating gesture.</summary>
    public void RecordGesture() => StartFreshOnNextSelection = false;
}
