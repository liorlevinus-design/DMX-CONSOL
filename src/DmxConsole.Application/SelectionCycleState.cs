namespace DmxConsole.Application;

/// <summary>
/// Shared operator selection-cycle state. Selection remains visibly active after an execution
/// (for example AT 50), but the next new Fixture/Group selection starts a fresh cycle and
/// therefore replaces the old one. This state belongs to the console Application layer rather
/// than a Razor component so touch, keyboard and future front-ends can share the same rule.
/// </summary>
public sealed class SelectionCycleState
{
    public bool StartFreshOnNextSelection { get; private set; }

    /// <summary>Called after a non-selection execution consumed the current targets.</summary>
    public void MarkExecutionCompleted() => StartFreshOnNextSelection = true;

    /// <summary>Called after the first successful selection mutation of a new/current cycle.</summary>
    public void MarkSelectionStarted() => StartFreshOnNextSelection = false;

    /// <summary>Used when an external GUI selection has already established the current cycle.</summary>
    public void MarkSelectionSynchronized() => StartFreshOnNextSelection = false;
}
