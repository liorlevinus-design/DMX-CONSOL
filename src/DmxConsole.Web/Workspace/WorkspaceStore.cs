namespace DmxConsole.Web.Workspaces;

/// <summary>Persists User/Show-scoped Workspaces. Factory Workspaces are never written here -
/// they're seeded in memory (WorkspaceViewModel.BuildDefaultWorkspace) and are read-only by
/// construction, so "Factory is never overwritten in place" is structural, not a rule callers
/// have to remember. Saving a Factory-scoped Workspace is the caller's job to turn into a
/// Duplicate-then-Save of a User copy first (see WorkspaceViewModel.SaveAsync) - this interface
/// only ever persists what it's given.</summary>
public interface IWorkspaceStore
{
    Task<IReadOnlyList<Workspace>> LoadAllAsync();
    Task SaveAsync(Workspace workspace);
    Task DeleteAsync(Guid workspaceId);
}

/// <summary>One JSON file per Workspace (via WorkspaceSerializer, so every file carries an
/// explicit schemaVersion - see that type's own doc comment), under the local machine's app-data
/// folder. This is server-side, per-machine state - consistent with the rest of this app's
/// "one console, shared by every connected browser/tablet" model (MainViewModel/WorkspaceViewModel
/// are both DI Singletons); it is deliberately NOT per-browser localStorage.</summary>
public sealed class LocalJsonWorkspaceStore : IWorkspaceStore
{
    private readonly string _directory;

    public LocalJsonWorkspaceStore(string? directoryOverride = null)
    {
        _directory = directoryOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DmxConsole", "Workspaces");
    }

    public async Task<IReadOnlyList<Workspace>> LoadAllAsync()
    {
        if (!Directory.Exists(_directory)) return Array.Empty<Workspace>();

        var results = new List<Workspace>();
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                string json = await File.ReadAllTextAsync(file);
                results.Add(WorkspaceSerializer.Deserialize(json));
            }
            catch (WorkspaceSchemaException)
            {
                // A malformed/unsupported-version file is skipped, not fatal to loading every
                // other saved workspace - never let one corrupt file take down the whole list.
                // (Surfacing this to the operator is a future diagnostics-UI concern; today it's
                // silently excluded rather than silently pretending it succeeded.)
            }
        }
        return results;
    }

    public async Task SaveAsync(Workspace workspace)
    {
        Directory.CreateDirectory(_directory);
        string json = WorkspaceSerializer.Serialize(workspace);
        await File.WriteAllTextAsync(PathFor(workspace.Id), json);
    }

    public Task DeleteAsync(Guid workspaceId)
    {
        string path = PathFor(workspaceId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathFor(Guid workspaceId) => Path.Combine(_directory, $"{workspaceId:N}.json");
}
