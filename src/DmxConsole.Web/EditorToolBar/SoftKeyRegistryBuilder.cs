namespace DmxConsole.Web.EditorToolBar;

/// <summary>
/// Builds the initial, real-but-minimal soft key trees for this first H1.6 slice (§16): Root,
/// FIXTURE, GROUP, CUE, and one nested CUE &gt; TIME. Every other object family from §4 is
/// deliberately left unregistered - SoftKeyRegistry.For() on those context ids just returns
/// empty, which EditorToolBar.razor renders as an empty (not broken) tool bar, honest about what
/// isn't built yet rather than faking a tree with dead buttons.
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
            NotImplemented("cue.delay", "Cue", "DELAY"),
            NotImplemented("cue.trigger", "Cue", "TRIGGER"),
            NotImplemented("cue.follow", "Cue", "FOLLOW"),
            NotImplemented("cue.wait", "Cue", "WAIT"),
            NotImplemented("cue.mark", "Cue", "MARK"),
            NotImplemented("cue.block", "Cue", "BLOCK"),
            NotImplemented("cue.label", "Cue", "LABEL"),
            ContextAction("cue.delete", "Cue", "DELETE", implemented: true),
        });

        registry.RegisterRange(new[]
        {
            NotImplemented("cue.time.general", "Cue.Time", "GENERAL"),
            NotImplemented("cue.time.intensity", "Cue.Time", "INTENSITY"),
            NotImplemented("cue.time.position", "Cue.Time", "POSITION"),
            NotImplemented("cue.time.color", "Cue.Time", "COLOR"),
            NotImplemented("cue.time.beam", "Cue.Time", "BEAM"),
            NotImplemented("cue.time.delay", "Cue.Time", "DELAY"),
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
