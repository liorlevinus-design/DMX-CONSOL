namespace DmxConsole.Web.EditorToolBar;

/// <summary>
/// One level of the Editor Tool Bar's context path - e.g. "CUE", or "CUE.TIME" nested under it.
/// A stack of these (see EditorContextStack) is the breadcrumb: CUE &gt; TIME &gt; POSITION.
/// Deliberately a plain immutable record, not scattered Razor booleans (H1.6 §6) - the same
/// shape is pushed by a View row click and by the Command Surface entering an object-type token
/// (H1.6 §19: one shared context system, not two).
/// </summary>
public sealed record EditorContextFrame
{
    /// <summary>Stable, dotted path key this frame's soft keys are registered under - e.g.
    /// "Cue", "Cue.Time", "Cue.Time.Position". SoftKeyRegistry.For(frame.Id) looks up exactly
    /// this.</summary>
    public required string Id { get; init; }

    /// <summary>Operator-facing breadcrumb label - e.g. "CUE", "TIME", "POSITION".</summary>
    public required string Label { get; init; }

    /// <summary>Which top-level object family this whole path belongs to - every frame in a
    /// stack shares the same ObjectType as the root frame that started it (entering a new object
    /// always replaces the stack, see EditorContextStack.EnterObject).</summary>
    public EditorObjectType ObjectType { get; init; } = EditorObjectType.None;

    /// <summary>The actual domain object currently in focus (a PatchedFixture/FixtureGroup/Cue/
    /// Executor/... reference, or null if the context was entered without a specific selected
    /// instance - e.g. typing "Fixture" on the Command Surface before a number is typed). Stable
    /// edit keys (STORE/UPDATE/DELETE/...) resolve what to act on from this, never from
    /// object-specific logic of their own (H1.6 §3).</summary>
    public object? SelectedObject { get; init; }

    /// <summary>Operator-facing label for SelectedObject - e.g. "Cue 12", "Group 5", "Fixture 3".
    /// Empty when SelectedObject is null.</summary>
    public string SelectedObjectLabel { get; init; } = string.Empty;
}
