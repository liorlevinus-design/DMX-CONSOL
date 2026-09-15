namespace DmxConsole.Web.EditorToolBar;

/// <summary>What a soft key actually does when pressed - kept small and generic on purpose so
/// EditorToolBar.razor never needs object-specific if-statements (H1.6 §3/§7): it just switches
/// on ActionType and, for EnterContext, pushes NextContextId/Label; every other ActionType is
/// resolved by EditorToolBarViewModel using the CURRENT EditorContextFrame (ObjectType +
/// SelectedObject), never anything hardcoded in the key itself.</summary>
public enum SoftKeyActionType
{
    /// <summary>Pushes a deeper sub-context (e.g. CUE -> CUE.TIME) - NextContextId/Label must be set.</summary>
    EnterContext,

    /// <summary>A stable edit-style action (Store/Update/Edit/Copy/Paste/Move/Delete/...) whose
    /// concrete behavior EditorToolBarViewModel resolves from the current context's ObjectType -
    /// same key id, context-dependent behavior (H1.6 §3's Cue/Group/Executor copy-paste example).</summary>
    ContextAction,

    /// <summary>Not wired to any real behavior yet - renders visibly disabled with a "not
    /// implemented yet" affordance rather than faking a result (H1.6 §16's explicit rule).</summary>
    NotImplemented,
}

/// <summary>
/// One soft key's definition - data, not Razor markup (H1.6 §7: "do not hardcode every toolbar
/// layout directly in Razor"). SoftKeyRegistry groups these by ContextId; EditorToolBar.razor
/// only ever renders whatever SoftKeyRegistry.For(currentContextId) returns.
/// </summary>
public sealed record SoftKeyDefinition
{
    public required string Id { get; init; }
    public required string Label { get; init; }

    /// <summary>The EditorContextFrame.Id this key appears under - e.g. "Fixture", "Cue", "Cue.Time".</summary>
    public required string ContextId { get; init; }

    public SoftKeyActionType ActionType { get; init; } = SoftKeyActionType.NotImplemented;

    /// <summary>For ActionType.EnterContext only - the id segment/label of the sub-context this key pushes.</summary>
    public string? NextContextIdSegment { get; init; }
    public string? NextContextLabel { get; init; }

    /// <summary>True only for a key EditorToolBarViewModel actually knows how to execute for at
    /// least one ObjectType right now. A ContextAction key can be Implemented for one object
    /// family and not yet for another (e.g. DELETE works for Cue/Group/Fixture already, not yet
    /// for Effect) - EditorToolBarViewModel's own per-press resolution is still the source of
    /// truth for whether THIS press does something; this flag only drives the key's default
    /// enabled/disabled rendering before that's known.</summary>
    public bool Implemented { get; init; }
}
