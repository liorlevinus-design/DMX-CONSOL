using DmxConsole.Core;
using DmxConsole.Web.Services;
using DmxConsole.Web.Workspaces;
using Xunit;

namespace DmxConsole.Web.Tests;

/// <summary>Milestone 1 continued (2026-09-16) - the Encoder Drawer's workspace-scoped
/// persistence: Workspace.EncoderDrawer round-trips through WorkspaceSerializer like any other
/// field, and WorkspaceViewModel.Changed fires so a sibling shell component can re-sync.</summary>
public class WorkspaceEncoderStateTests
{
    private static Workspace BuildWorkspace()
    {
        var surface = new WorkspaceSurface { Name = "Main", Root = new TabPaneNode() };
        return new Workspace { Name = "Test", Surfaces = { surface } };
    }

    [Fact]
    public void EncoderDrawerState_RoundTrips_ThroughSerializer()
    {
        var original = BuildWorkspace();
        original.EncoderDrawer.IsOpen = false;
        original.EncoderDrawer.ActiveCategory = EncoderCategory.Color;
        original.EncoderDrawer.Page = 2;

        var restored = WorkspaceSerializer.Deserialize(WorkspaceSerializer.Serialize(original));

        Assert.False(restored.EncoderDrawer.IsOpen);
        Assert.Equal(EncoderCategory.Color, restored.EncoderDrawer.ActiveCategory);
        Assert.Equal(2, restored.EncoderDrawer.Page);
    }

    [Fact]
    public void EncoderDrawerState_DefaultsToOpen_NullCategory_ZeroPage()
    {
        var workspace = BuildWorkspace();

        Assert.True(workspace.EncoderDrawer.IsOpen);
        Assert.Null(workspace.EncoderDrawer.ActiveCategory);
        Assert.Equal(0, workspace.EncoderDrawer.Page);
    }

    [Fact]
    public void OldDocument_WithoutEncoderDrawerField_LoadsWithDefaults_DoesNotThrow()
    {
        // Simulates a workspace JSON saved before EncoderDrawerState existed - System.Text.Json
        // must fill in Workspace.EncoderDrawer's own default (new()), never throw or silently
        // drop the rest of the document.
        string json = "{\"schemaVersion\": " + WorkspaceSerializer.CurrentSchemaVersion + ", \"workspace\": {"
            + "\"Id\": \"11111111-1111-1111-1111-111111111111\","
            + "\"Name\": \"Legacy\","
            + "\"Scope\": 1,"
            + "\"Surfaces\": [{\"Id\": \"22222222-2222-2222-2222-222222222222\", \"Name\": \"Main\", \"Root\": "
            + "{\"$type\": \"tabs\", \"Id\": \"33333333-3333-3333-3333-333333333333\", \"Tabs\": [], \"ActiveTabId\": \"00000000-0000-0000-0000-000000000000\"}}]"
            + "}}";

        var restored = WorkspaceSerializer.Deserialize(json);

        Assert.Equal("Legacy", restored.Name);
        Assert.NotNull(restored.EncoderDrawer);
        Assert.True(restored.EncoderDrawer.IsOpen);
    }

    [Fact]
    public void DuplicateWorkspace_CarriesOverEncoderDrawerState()
    {
        var original = BuildWorkspace();
        original.EncoderDrawer.ActiveCategory = EncoderCategory.Shape;
        original.EncoderDrawer.Page = 1;

        var duplicate = new WorkspaceLayoutService().DuplicateWorkspace(original);

        Assert.Equal(EncoderCategory.Shape, duplicate.EncoderDrawer.ActiveCategory);
        Assert.Equal(1, duplicate.EncoderDrawer.Page);
    }

    [Fact]
    public void WorkspaceViewModel_Changed_FiresOnSwitchTo()
    {
        var vm = new WorkspaceViewModel(new InMemoryWorkspaceStore());
        vm.NewWorkspace();
        var second = vm.Current;
        vm.SwitchTo(vm.Workspaces[0].Id); // back to the Factory workspace

        int fired = 0;
        vm.Changed += () => fired++;

        vm.SwitchTo(second.Id);

        Assert.Equal(1, fired);
        Assert.Equal(second.Id, vm.Current.Id);
    }

    [Fact]
    public void WorkspaceViewModel_Changed_DoesNotFire_WhenSwitchingToUnknownId()
    {
        var vm = new WorkspaceViewModel(new InMemoryWorkspaceStore());
        int fired = 0;
        vm.Changed += () => fired++;

        vm.SwitchTo(Guid.NewGuid());

        Assert.Equal(0, fired);
    }

    private sealed class InMemoryWorkspaceStore : IWorkspaceStore
    {
        public Task<IReadOnlyList<Workspace>> LoadAllAsync() => Task.FromResult<IReadOnlyList<Workspace>>(Array.Empty<Workspace>());
        public Task SaveAsync(Workspace workspace) => Task.CompletedTask;
        public Task DeleteAsync(Guid workspaceId) => Task.CompletedTask;
    }
}
