using DmxConsole.Web.Workspaces;

namespace DmxConsole.Web.Tests;

public class WorkspaceStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "DmxConsoleWorkspaceStoreTests_" + Guid.NewGuid().ToString("N"));

    private LocalJsonWorkspaceStore CreateStore() => new(_tempDir);

    private static Workspace BuildWorkspace(string name, WorkspaceScope scope = WorkspaceScope.User)
    {
        var surface = new WorkspaceSurface { Name = "Main", Root = new TabPaneNode() };
        return new Workspace { Name = name, Scope = scope, Surfaces = { surface } };
    }

    [Fact]
    public async Task SaveThenLoadAll_ReturnsTheSameWorkspace()
    {
        var store = CreateStore();
        var workspace = BuildWorkspace("Saved One");

        await store.SaveAsync(workspace);
        var loaded = await store.LoadAllAsync();

        var found = Assert.Single(loaded);
        Assert.Equal(workspace.Id, found.Id);
        Assert.Equal("Saved One", found.Name);
    }

    [Fact]
    public async Task Delete_RemovesTheWorkspace()
    {
        var store = CreateStore();
        var workspace = BuildWorkspace("To Delete");
        await store.SaveAsync(workspace);

        await store.DeleteAsync(workspace.Id);
        var loaded = await store.LoadAllAsync();

        Assert.Empty(loaded);
    }

    [Fact]
    public async Task SaveAsync_Twice_OverwritesTheSameFile_NotDuplicated()
    {
        var store = CreateStore();
        var workspace = BuildWorkspace("Original Name");
        await store.SaveAsync(workspace);

        workspace.Name = "Renamed";
        await store.SaveAsync(workspace);

        var loaded = await store.LoadAllAsync();
        var found = Assert.Single(loaded);
        Assert.Equal("Renamed", found.Name);
    }

    [Fact]
    public async Task LoadAllAsync_OnMissingDirectory_ReturnsEmpty_NotThrow()
    {
        var store = CreateStore(); // directory never created - no SaveAsync call yet

        var loaded = await store.LoadAllAsync();

        Assert.Empty(loaded);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }
}
