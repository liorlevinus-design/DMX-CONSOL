namespace DmxConsole.Web.EditorToolBar;

/// <summary>
/// Builds the real-but-still-partial soft key trees for H1.6: Root, FIXTURE, GROUP, CUE, nested
/// CUE &gt; TIME (Vector's actual Time mode, H1.6 Slice 2) and CUE &gt; STORE OPTIONS (Vector's
/// five Store Options meanings, also Slice 2). Every other object family from §4 is deliberately
/// left unregistered - SoftKeyRegistry.For() on those context ids just returns empty, which
/// EditorToolBar.razor renders as an empty (not broken) tool bar, honest about what isn't built
/// yet rather than faking a tree with dead buttons.
/// </summary>
public static class SoftKeyRegistryBuilder
{
    public static SoftKeyRegistry Build()
    {
        var registry = new SoftKeyRegistry();

        registry.RegisterRange(new[]
        {
            EnterContext("root.fixture", "Root", nameof(EditorObjectType.Fixture), "FIXTURE"),
            EnterContext("root.group", "Root", nameof(EditorObjectType.Group), "GROUP"),
            EnterContext("root.cue", "Root", nameof(EditorObjectType.Cue), "CUE"),
        });

        registry.RegisterRange(new[]
        {
            NotImplemented("fixture.intensity", "Fixture", "INTENSITY"),
            NotImplemented("fixture.position", "Fixture", "POSITION"),
            NotImplemented("fixture.color", "Fixture", "COLOR"),
            NotImplemented("fixture.beam", "Fixture", "BEAM"),
            NotImplemented("fixture.home", "Fixture", "HOME"),
            NotImplemented("fixture.highlight", "Fixture", "HIGHLIGHT"),
            NotImplemented("fixture.lowlight", "Fixture", "LOWLIGHT"),
            NotImplemented("fixture.fan", "Fixture", "FAN"),
            ContextAction("fixture.odd", "Fixture", "ODD", implemented: true),
            ContextAction("fixture.even", "Fixture", "EVEN", implemented: true),
            ContextAction("fixture.delete", "Fixture", "DELETE", implemented: true),
        });

        registry.RegisterRange(new[]
        {
            ContextAction("group.store", "Group", "STORE", implemented: true),
            ContextAction("group.update", "Group", "UPDATE", implemented: true),
            NotImplemented("group.overwrite", "Group", "OVERWRITE"),
            NotImplemented("group.rename", "Group", "RENAME"),
            ContextAction("group.delete", "Group", "DELETE", implemented: true),
            NotImplemented("group.odd", "Group", "ODD"),
            NotImplemented("group.even", "Group", "EVEN"),
            NotImplemented("group.invert", "Group", "INVERT"),
        });

        registry.RegisterRange(new[]
        {
            ContextAction("cue.store", "Cue", "STORE", implemented: true),
            ContextAction("cue.update", "Cue", "UPDATE", implemented: true),
            NotImplemented("cue.edit", "Cue", "EDIT"),
            EnterContext("cue.time", "Cue", "Time", "TIME"),
            EnterContext("cue.storeoptions", "Cue", "StoreOptions", "STORE OPTIONS"),
            NotImplemented("cue.mark", "Cue", "MARK"),
            NotImplemented("cue.block", "Cue", "BLOCK"),
            NotImplemented("cue.label", "Cue", "LABEL"),
            ContextAction("cue.delete", "Cue", "DELETE", implemented: true),
        });

        // H1.6 Slice 2 - matches Vector's actual Time mode (docs/VECTOR_EDITOR_TOOLBAR_REFERENCE.md
        // §3), replacing the earlier grandMA3-style GENERAL/INTENSITY/POSITION/COLOR/BEAM tree.
        // TIME-IN/TIME-OUT/DELAY-IN/DELAY-OUT/WAIT stay honestly NotImplemented HERE - entering a
        // numeric value into a context key needs CommandSurface/CommandComposer numeric-entry
        // vocabulary that doesn't exist yet (same pre-existing gap Phase H1.5 already documented,
        // not a new one). Real editing of those five values happens through CueListPanel's form.
        // FOLLOW ON / MANUAL need no numeric entry - simple toggles, wired for real below.
        registry.RegisterRange(new[]
        {
            NotImplemented("cue.time.timein", "Cue.Time", "TIME-IN"),
            NotImplemented("cue.time.timeout", "Cue.Time", "TIME-OUT"),
            NotImplemented("cue.time.delayin", "Cue.Time", "DELAY-IN"),
            NotImplemented("cue.time.delayout", "Cue.Time", "DELAY-OUT"),
            NotImplemented("cue.time.wait", "Cue.Time", "WAIT"),
            ContextAction("cue.time.followon", "Cue.Time", "FOLLOW ON", implemented: true),
            ContextAction("cue.time.manual", "Cue.Time", "MANUAL", implemented: true),
        });

        // H1.6 Slice 2 - Vector's Store Options mode (only reachable from CUE, p.112 of the
        // manual): what counts as "part of this Store" is an explicit operator choice, not always
        // the equivalent of ALL STAGE. Each button sets the filter AND stores in one press.
        registry.RegisterRange(new[]
        {
            ContextAction("cue.storeoptions.alleditor", "Cue.StoreOptions", "ALL EDITOR", implemented: true),
            ContextAction("cue.storeoptions.activeonly", "Cue.StoreOptions", "ACTIVE ONLY", implemented: true),
            ContextAction("cue.storeoptions.allstage", "Cue.StoreOptions", "ALL STAGE", implemented: true),
            ContextAction("cue.storeoptions.allparamsforselected", "Cue.StoreOptions", "ALL PARAMS FOR SELECTED", implemented: true),
            ContextAction("cue.storeoptions.allparamsifactive", "Cue.StoreOptions", "ALL PARAMS IF ACTIVE", implemented: true),
        });

        // Stable edit keys (H1.6 §3) present across every real context this slice - EDIT/COPY/
        // PASTE/MOVE aren't wired to real behavior yet (kept honestly NotImplemented); STORE/
        // UPDATE/DELETE are registered per-context above since their semantics genuinely differ
        // enough per object family (e.g. Group also gets RENAME/OVERWRITE) to define individually
        // rather than force a one-size row - but they're still resolved generically in
        // EditorToolBarViewModel.Press, never hardcoded per key.
        foreach (var contextId in new[] { "Fixture", "Group", "Cue" })
        {
            registry.RegisterRange(new[]
            {
                NotImplemented($"{contextId.ToLowerInvariant()}.edit", contextId, "EDIT"),
                NotImplemented($"{contextId.ToLowerInvariant()}.copy", contextId, "COPY"),
                NotImplemented($"{contextId.ToLowerInvariant()}.paste", contextId, "PASTE"),
                NotImplemented($"{contextId.ToLowerInvariant()}.move", contextId, "MOVE"),
            });
        }

        return registry;
    }

    private static SoftKeyDefinition EnterContext(string id, string contextId, string nextIdSegment, string nextLabel) => new()
    {
        Id = id,
        Label = nextLabel,
        ContextId = contextId,
        ActionType = SoftKeyActionType.EnterContext,
        NextContextIdSegment = nextIdSegment,
        NextContextLabel = nextLabel,
        Implemented = true,
    };

    private static SoftKeyDefinition ContextAction(string id, string contextId, string label, bool implemented) => new()
    {
        Id = id,
        Label = label,
        ContextId = contextId,
        ActionType = SoftKeyActionType.ContextAction,
        Implemented = implemented,
    };

    private static SoftKeyDefinition NotImplemented(string id, string contextId, string label) => new()
    {
        Id = id,
        Label = label,
        ContextId = contextId,
        ActionType = SoftKeyActionType.NotImplemented,
        Implemented = false,
    };
}
