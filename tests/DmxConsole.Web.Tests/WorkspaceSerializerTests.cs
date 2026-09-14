using DmxConsole.Web.Workspaces;

namespace DmxConsole.Web.Tests;

public class WorkspaceSerializerTests
{
    private static Workspace BuildNestedWorkspace()
    {
        var viewA = new ViewInstance { Kind = ViewKind.CueList, Title = "Cues" };
        var viewB = new ViewInstance { Kind = ViewKind.Effects, Title = "Effects" };
        var viewC = new ViewInstance { Kind = ViewKind.Patch, Title = "Patch" };

        var leftTabPane = new TabPaneNode { Tabs = { viewA }, ActiveTabId = viewA.Id };
        var rightTopTabPane = new TabPaneNode { Tabs = { viewB }, ActiveTabId = viewB.Id };
        var rightBottomTabPane = new TabPaneNode { Tabs = { viewC }, ActiveTabId = viewC.Id };
        var rightSplit = new SplitNode { Orientation = SplitOrientation.Vertical, Ratio = 0.4, ChildA = rightTopTabPane, ChildB = rightBottomTabPane };
        var rootSplit = new SplitNode { Orientation = SplitOrientation.Horizontal, Ratio = 0.6, ChildA = leftTabPane, ChildB = rightSplit };

        var surface = new WorkspaceSurface { Name = "Main", Root = rootSplit };
        return new Workspace { Name = "Nested Test", Surfaces = { surface } };
    }

    [Fact]
    public void RoundTrip_PreservesNestedTreeStructure()
    {
        var original = BuildNestedWorkspace();

        var json = WorkspaceSerializer.Serialize(original);
        var restored = WorkspaceSerializer.Deserialize(json);

        var restoredRootSplit = Assert.IsType<SplitNode>(restored.Surfaces[0].Root);
        Assert.Equal(SplitOrientation.Horizontal, restoredRootSplit.Orientation);
        Assert.Equal(0.6, restoredRootSplit.Ratio);
        Assert.IsType<TabPaneNode>(restoredRootSplit.ChildA);
        var restoredRightSplit = Assert.IsType<SplitNode>(restoredRootSplit.ChildB);
        Assert.Equal(SplitOrientation.Vertical, restoredRightSplit.Orientation);
        Assert.IsType<TabPaneNode>(restoredRightSplit.ChildA);
        Assert.IsType<TabPaneNode>(restoredRightSplit.ChildB);
    }

    [Fact]
    public void RoundTrip_PreservesStableIds_ForWorkspaceSurfacePaneAndView()
    {
        var original = BuildNestedWorkspace();
        var originalLeftTabPane = (TabPaneNode)((SplitNode)original.Surfaces[0].Root).ChildA;
        var originalView = originalLeftTabPane.Tabs[0];

        var restored = WorkspaceSerializer.Deserialize(WorkspaceSerializer.Serialize(original));
        var restoredLeftTabPane = (TabPaneNode)((SplitNode)restored.Surfaces[0].Root).ChildA;

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Surfaces[0].Id, restored.Surfaces[0].Id);
        Assert.Equal(originalLeftTabPane.Id, restoredLeftTabPane.Id);
        Assert.Equal(originalView.Id, restoredLeftTabPane.Tabs[0].Id);
        Assert.Equal(originalView.Id, restoredLeftTabPane.ActiveTabId);
    }

    [Fact]
    public void Serialize_IncludesCurrentSchemaVersion_AndRoundTrips()
    {
        var original = BuildNestedWorkspace();

        var json = WorkspaceSerializer.Serialize(original);

        Assert.Contains("\"schemaVersion\"", json);
        Assert.Contains($": {WorkspaceSerializer.CurrentSchemaVersion}", json);

        var restored = WorkspaceSerializer.Deserialize(json); // does not throw
        Assert.Equal(original.Name, restored.Name);
    }

    [Fact]
    public void Deserialize_NewerUnsupportedSchemaVersion_ThrowsClearly()
    {
        string json = $"{{\"schemaVersion\": {WorkspaceSerializer.CurrentSchemaVersion + 1}, \"workspace\": {{}}}}";

        var ex = Assert.Throws<WorkspaceSchemaException>(() => WorkspaceSerializer.Deserialize(json));
        Assert.Contains("newer", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_OlderUnsupportedSchemaVersion_ThrowsClearly()
    {
        string json = "{\"schemaVersion\": 0, \"workspace\": {}}";

        Assert.Throws<WorkspaceSchemaException>(() => WorkspaceSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_MalformedJson_ThrowsClearly_NeverSilentlyReturnsEmptyWorkspace()
    {
        string malformed = "{ this is not valid json ";

        Assert.Throws<WorkspaceSchemaException>(() => WorkspaceSerializer.Deserialize(malformed));
    }

    [Fact]
    public void Deserialize_MissingSchemaVersion_ThrowsClearly()
    {
        string json = "{\"workspace\": {}}";

        Assert.Throws<WorkspaceSchemaException>(() => WorkspaceSerializer.Deserialize(json));
    }

    [Fact]
    public void DuplicateWorkspace_ProducesNewIdsThroughout_AndUserScope()
    {
        var original = BuildNestedWorkspace();
        original.Scope = WorkspaceScope.Factory;
        var originalLeftTabPane = (TabPaneNode)((SplitNode)original.Surfaces[0].Root).ChildA;
        var originalViewId = originalLeftTabPane.Tabs[0].Id;

        var duplicate = new WorkspaceLayoutService().DuplicateWorkspace(original);

        Assert.NotEqual(original.Id, duplicate.Id);
        Assert.NotEqual(original.Surfaces[0].Id, duplicate.Surfaces[0].Id);
        Assert.Equal(WorkspaceScope.User, duplicate.Scope);
        var duplicateLeftTabPane = (TabPaneNode)((SplitNode)duplicate.Surfaces[0].Root).ChildA;
        Assert.NotEqual(originalLeftTabPane.Id, duplicateLeftTabPane.Id);
        Assert.NotEqual(originalViewId, duplicateLeftTabPane.Tabs[0].Id);
        Assert.Equal(duplicateLeftTabPane.Tabs[0].Id, duplicateLeftTabPane.ActiveTabId); // ActiveTabId remapped consistently
    }
}
