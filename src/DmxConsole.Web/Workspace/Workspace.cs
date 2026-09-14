namespace DmxConsole.Web.Workspaces;

public enum WorkspaceScope { Factory, User, Show }

/// <summary>One "screen" of panes within a Workspace - multi-surface-ready from v1 so adding
/// external-monitor support later never requires re-modeling persistence. This milestone only
/// ever creates one Surface, named "Main" - no multi-monitor UI yet, just the shape that won't
/// need a rewrite when that lands.</summary>
public sealed class WorkspaceSurface
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public PaneNode Root { get; set; } = null!;
}

/// <summary>A saved arrangement of Views. Factory workspaces ship read-only (Save on one implicitly
/// Save-As's a new User-scoped copy - see WorkspaceLayoutService.DuplicateWorkspace /
/// IWorkspaceStore.SaveAsync); User workspaces are the operator's own; Show is reserved for a
/// future per-show-file scope, not distinguished from User in this milestone's persistence yet.</summary>
public sealed class Workspace
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public WorkspaceScope Scope { get; set; } = WorkspaceScope.User;
    public List<WorkspaceSurface> Surfaces { get; set; } = new();

    /// <summary>When true, every structural mutation (Split/Close/Resize/Move/Maximize) offered by
    /// WorkspaceLayoutService fails gracefully instead of mutating the tree - prevents accidental
    /// layout changes during live operation.</summary>
    public bool LayoutLocked { get; set; }
}
