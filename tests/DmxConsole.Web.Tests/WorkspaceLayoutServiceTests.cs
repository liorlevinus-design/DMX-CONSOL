using DmxConsole.Web.Workspaces;

namespace DmxConsole.Web.Tests;

public class WorkspaceLayoutServiceTests
{
    private static (Workspace Workspace, WorkspaceSurface Surface, WorkspaceLayoutService Service) BuildSingleTabWorkspace()
    {
        var view = new ViewInstance { Kind = ViewKind.CueList, Title = "Cues" };
        var root = new TabPaneNode();
        root.Tabs.Add(view);
        root.ActiveTabId = view.Id;

        var surface = new WorkspaceSurface { Name = "Main", Root = root };
        var workspace = new Workspace { Name = "Test", Surfaces = { surface } };
        return (workspace, surface, new WorkspaceLayoutService());
    }

    [Fact]
    public void SplitPane_Horizontal_CreatesSplitWithOriginalAsChildA()
    {
        var (workspace, surface, service) = BuildSingleTabWorkspace();
        var originalRootId = surface.Root.Id;

        bool ok = service.SplitPane(workspace, surface, originalRootId, SplitOrientation.Horizontal);

        Assert.True(ok);
        var split = Assert.IsType<SplitNode>(surface.Root);
        Assert.Equal(SplitOrientation.Horizontal, split.Orientation);
        Assert.Equal(originalRootId, split.ChildA.Id);
        Assert.IsType<TabPaneNode>(split.ChildB);
    }

    [Fact]
    public void SplitPane_Vertical_SetsOrientation()
    {
        var (workspace, surface, service) = BuildSingleTabWorkspace();

        service.SplitPane(workspace, surface, surface.Root.Id, SplitOrientation.Vertical);

        var split = Assert.IsType<SplitNode>(surface.Root);
        Assert.Equal(SplitOrientation.Vertical, split.Orientation);
    }

    [Fact]
    public void ResizeSplit_ClampsAndSetsRatio()
    {
        var (workspace, surface, service) = BuildSingleTabWorkspace();
        service.SplitPane(workspace, surface, surface.Root.Id, SplitOrientation.Horizontal);
        var split = (SplitNode)surface.Root;

        Assert.True(service.ResizeSplit(workspace, surface, split.Id, 0.3));
        Assert.Equal(0.3, split.Ratio);

        service.ResizeSplit(workspace, surface, split.Id, 5.0); // out of range - clamped, not thrown
        Assert.True(split.Ratio <= 0.95);
    }

    [Fact]
    public void ClosePane_PromotesSibling()
    {
        var (workspace, surface, service) = BuildSingleTabWorkspace();
        var originalTabPaneId = surface.Root.Id;
        service.SplitPane(workspace, surface, originalTabPaneId, SplitOrientation.Horizontal);
        var split = (SplitNode)surface.Root;
        var siblingId = split.ChildB.Id;

        bool ok = service.ClosePane(workspace, surface, originalTabPaneId);

        Assert.True(ok);
        Assert.Equal(siblingId, surface.Root.Id); // sibling promoted to Root, SplitNode gone
    }

    [Fact]
    public void CloseView_WithMultipleTabs_PreservesRemainingTabs()
    {
        var (workspace, surface, service) = BuildSingleTabWorkspace();
        var tabPane = (TabPaneNode)surface.Root;
        var secondView = new ViewInstance { Kind = ViewKind.Effects, Title = "Effects" };
        service.AddView(workspace, surface, tabPane.Id, secondView);
        var firstViewId = tabPane.Tabs[0].Id;

        bool ok = service.CloseView(workspace, surface, firstViewId);

        Assert.True(ok);
        Assert.Single(tabPane.Tabs);
        Assert.Equal(secondView.Id, tabPane.Tabs[0].Id);
    }

    [Fact]
    public void CloseView_LastTabInPane_CollapsesPaneAndPromotesSibling()
    {
        var (workspace, surface, service) = BuildSingleTabWorkspace();
        var originalTabPaneId = surface.Root.Id;
        var onlyViewId = ((TabPaneNode)surface.Root).Tabs[0].Id;
        service.SplitPane(workspace, surface, originalTabPaneId, SplitOrientation.Horizontal);
        var split = (SplitNode)surface.Root;
        var siblingId = split.ChildB.Id;

        bool ok = service.CloseView(workspace, surface, onlyViewId);

        Assert.True(ok);
        Assert.Equal(siblingId, surface.Root.Id); // the now-empty pane collapsed, sibling promoted
    }

    [Fact]
    public void CloseView_KeepsActiveTabIdValid_WhenActiveTabIsClosed()
    {
        var (workspace, surface, service) = BuildSingleTabWorkspace();
        var tabPane = (TabPaneNode)surface.Root;
        var secondView = new ViewInstance { Kind = ViewKind.Effects, Title = "Effects" };
        service.AddView(workspace, surface, tabPane.Id, secondView); // AddView also makes it active
        service.SetActiveTab(surface, tabPane.Id, tabPane.Tabs[0].Id); // make the FIRST tab active again
        var activeId = tabPane.Tabs[0].Id;

        service.CloseView(workspace, surface, activeId);

        Assert.NotEqual(Guid.Empty, tabPane.ActiveTabId);
        Assert.Contains(tabPane.Tabs, t => t.Id == tabPane.ActiveTabId);
    }

    [Fact]
    public void LayoutLocked_RejectsStructuralMutation()
    {
        var (workspace, surface, service) = BuildSingleTabWorkspace();
        service.LockLayout(workspace);
        var rootId = surface.Root.Id;

        Assert.False(service.SplitPane(workspace, surface, rootId, SplitOrientation.Horizontal));
        Assert.False(service.ClosePane(workspace, surface, rootId));
        Assert.False(service.AddView(workspace, surface, rootId, new ViewInstance { Kind = ViewKind.Patch }));
        Assert.IsType<TabPaneNode>(surface.Root); // structurally untouched

        service.UnlockLayout(workspace);
        Assert.True(service.SplitPane(workspace, surface, rootId, SplitOrientation.Horizontal));
    }

    [Fact]
    public void ViewRegistry_UnknownKind_ReturnsFalse_NeverThrows()
    {
        var registry = new ViewRegistry();
        registry.Register(new ViewDescriptor(ViewKind.CueList, "Cues", typeof(object)));

        bool found = registry.TryGet(ViewKind.Effects, out _);

        Assert.False(found);
    }
}
