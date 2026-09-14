using DmxConsole.Web.Workspaces;

namespace DmxConsole.Web.Services;

/// <summary>
/// The shared Workspace shell state - one instance for the whole app (Singleton, same pattern
/// as MainViewModel: every connected browser/tablet looks at the same live layout, exactly like
/// a real console with multiple control surfaces). Holds the ViewRegistry (what View kinds this
/// build knows how to host), the WorkspaceLayoutService (pure tree-mutation logic, no UI
/// dependency), and the single active Workspace for this milestone (multi-workspace
/// switching/persistence lands in Slice 5).
/// </summary>
public sealed class WorkspaceViewModel
{
    public ViewRegistry Registry { get; } = new();
    public WorkspaceLayoutService Layout { get; } = new();
    public Workspace Current { get; private set; }

    public WorkspaceViewModel()
    {
        Current = BuildDefaultWorkspace();
    }

    /// <summary>Slice 2's shell has nothing real to host yet - a single empty pane. Slice 3
    /// registers a real View (CueList) and this default grows a first tab for it.</summary>
    private static Workspace BuildDefaultWorkspace()
    {
        var surface = new WorkspaceSurface { Name = "Main", Root = new TabPaneNode() };
        return new Workspace { Name = "Default", Scope = WorkspaceScope.Factory, Surfaces = { surface } };
    }
}
