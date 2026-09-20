using DmxConsole.Web.Services;
using DmxConsole.Web.Workspaces;
using Xunit;

namespace DmxConsole.Web.Tests;

/// <summary>Legacy UI cleanup slice - ProgrammerPanel was removed as a UI surface and
/// ViewKind.Programmer is no longer registered, but a Workspace saved before that change can
/// still contain ViewInstance entries with that Kind. WorkspaceViewModel.InitializeAsync must
/// remap them to ViewKind.Presets so the pane opens to a real view instead of PaneHost.razor's
/// "Unknown view" dead end.</summary>
public class WorkspaceLegacyViewMigrationTests
{
    [Fact]
    public async Task InitializeAsync_RemapsTopLevelLegacyProgrammerTab_ToPresets()
    {
        var legacyTab = new ViewInstance { Kind = ViewKind.Programmer, Title = "Programmer" };
        var tabPane = new TabPaneNode { Tabs = { legacyTab }, ActiveTabId = legacyTab.Id };
        var surface = new WorkspaceSurface { Name = "Main", Root = tabPane };
        var saved = new Workspace { Name = "Old Layout", Scope = WorkspaceScope.User, Surfaces = { surface } };

        var vm = new WorkspaceViewModel(new InMemoryWorkspaceStore(saved));
        await vm.InitializeAsync();

        var loaded = Assert.Single(vm.Workspaces, w => w.Name == "Old Layout");
        var restoredTabPane = Assert.IsType<TabPaneNode>(loaded.Surfaces[0].Root);
        var restoredTab = Assert.Single(restoredTabPane.Tabs);

        Assert.Equal(ViewKind.Presets, restoredTab.Kind);
        Assert.Equal("Presets", restoredTab.Title);
        // Id (and therefore ActiveTabId's target) must survive the remap - a tab is never "closed
        // and reopened" by this fixup, just repointed to a different Kind.
        Assert.Equal(legacyTab.Id, restoredTab.Id);
        Assert.Equal(legacyTab.Id, restoredTabPane.ActiveTabId);
    }

    [Fact]
    public async Task InitializeAsync_RemapsLegacyProgrammerTab_NestedInsideASplit()
    {
        var legacyTab = new ViewInstance { Kind = ViewKind.Programmer, Title = "Programmer" };
        var bottomLeft = new TabPaneNode { Tabs = { legacyTab }, ActiveTabId = legacyTab.Id };
        var topLeft = new TabPaneNode();
        var split = new SplitNode { ChildA = topLeft, ChildB = bottomLeft };
        var surface = new WorkspaceSurface { Name = "Main", Root = split };
        var saved = new Workspace { Name = "Nested Old Layout", Surfaces = { surface } };

        var vm = new WorkspaceViewModel(new InMemoryWorkspaceStore(saved));
        await vm.InitializeAsync();

        var loaded = Assert.Single(vm.Workspaces, w => w.Name == "Nested Old Layout");
        var restoredSplit = Assert.IsType<SplitNode>(loaded.Surfaces[0].Root);
        var restoredBottomLeft = Assert.IsType<TabPaneNode>(restoredSplit.ChildB);

        Assert.Equal(ViewKind.Presets, Assert.Single(restoredBottomLeft.Tabs).Kind);
    }

    [Fact]
    public async Task InitializeAsync_LeavesACustomRenamedLegacyTab_TitleAlone()
    {
        // An operator who renamed the tab away from the default "Programmer" label gets that
        // choice preserved - only the Kind (which view actually renders) changes.
        var legacyTab = new ViewInstance { Kind = ViewKind.Programmer, Title = "My Favorites" };
        var tabPane = new TabPaneNode { Tabs = { legacyTab }, ActiveTabId = legacyTab.Id };
        var surface = new WorkspaceSurface { Name = "Main", Root = tabPane };
        var saved = new Workspace { Name = "Renamed Tab Layout", Surfaces = { surface } };

        var vm = new WorkspaceViewModel(new InMemoryWorkspaceStore(saved));
        await vm.InitializeAsync();

        var loaded = Assert.Single(vm.Workspaces, w => w.Name == "Renamed Tab Layout");
        var restoredTab = Assert.Single(((TabPaneNode)loaded.Surfaces[0].Root).Tabs);

        Assert.Equal(ViewKind.Presets, restoredTab.Kind);
        Assert.Equal("My Favorites", restoredTab.Title);
    }

    [Fact]
    public async Task InitializeAsync_DoesNotTouchWorkspacesWithoutLegacyViews()
    {
        var tab = new ViewInstance { Kind = ViewKind.Channels, Title = "Channels" };
        var tabPane = new TabPaneNode { Tabs = { tab }, ActiveTabId = tab.Id };
        var surface = new WorkspaceSurface { Name = "Main", Root = tabPane };
        var saved = new Workspace { Name = "Modern Layout", Surfaces = { surface } };

        var vm = new WorkspaceViewModel(new InMemoryWorkspaceStore(saved));
        await vm.InitializeAsync();

        var loaded = Assert.Single(vm.Workspaces, w => w.Name == "Modern Layout");
        Assert.Equal(ViewKind.Channels, Assert.Single(((TabPaneNode)loaded.Surfaces[0].Root).Tabs).Kind);
    }

    private sealed class InMemoryWorkspaceStore : IWorkspaceStore
    {
        private readonly IReadOnlyList<Workspace> _workspaces;
        public InMemoryWorkspaceStore(params Workspace[] workspaces) => _workspaces = workspaces;
        public Task<IReadOnlyList<Workspace>> LoadAllAsync() => Task.FromResult(_workspaces);
        public Task SaveAsync(Workspace workspace) => Task.CompletedTask;
        public Task DeleteAsync(Guid workspaceId) => Task.CompletedTask;
    }
}
