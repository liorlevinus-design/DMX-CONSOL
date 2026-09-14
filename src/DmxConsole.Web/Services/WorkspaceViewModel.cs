using DmxConsole.Web.Components.ConsoleUi;
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
        RegisterViews();
        Current = BuildDefaultWorkspace();
    }

    /// <summary>Slice 3: the first existing console View (CueListPanel) becomes hostable by the
    /// Workspace shell, unchanged - proving the architecture, not redesigning the View. More
    /// kinds get registered as later slices migrate the remaining fixed tabs (Slice 7 of the
    /// milestone's own sequencing).</summary>
    private void RegisterViews()
    {
        Registry.Register(new ViewDescriptor(ViewKind.CueList, "Cues", typeof(CueListPanel)));
    }

    /// <summary>Slice 3: the default Workspace now opens with one tab hosting CueListPanel, so
    /// the preview route has something real to show instead of an empty pane.</summary>
    private static Workspace BuildDefaultWorkspace()
    {
        var cueListTab = new ViewInstance { Kind = ViewKind.CueList, Title = "Cues" };
        var tabPane = new TabPaneNode { Tabs = { cueListTab }, ActiveTabId = cueListTab.Id };
        var surface = new WorkspaceSurface { Name = "Main", Root = tabPane };
        return new Workspace { Name = "Default", Scope = WorkspaceScope.Factory, Surfaces = { surface } };
    }
}
