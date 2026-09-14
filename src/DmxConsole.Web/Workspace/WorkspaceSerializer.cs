using System.Text.Json;
using System.Text.Json.Serialization;

namespace DmxConsole.Web.Workspaces;

/// <summary>Thrown for any workspace document System.Text.Json's failure/mismatch. Distinct
/// type - never a bare exception - so a caller (IWorkspaceStore, WorkspaceLayoutService.Duplicate,
/// tests) can catch it specifically instead of masking an unrelated bug as "just corrupt data".</summary>
public sealed class WorkspaceSchemaException : Exception
{
    public WorkspaceSchemaException(string message) : base(message) { }
    public WorkspaceSchemaException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// The only place that turns a Workspace into JSON and back. Every document carries an explicit
/// schemaVersion - deliberately not "just serialize the class" - so a future model change has a
/// real place to add a migration instead of silently corrupting or replacing a user's saved
/// layout. No migration framework is needed yet (v1 is the only version that has ever shipped);
/// WorkspaceMigration is the narrow, explicit extension point for when a v2 arrives.
/// </summary>
public static class WorkspaceSerializer
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private sealed record WorkspaceDocument(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("workspace")] Workspace Workspace);

    public static string Serialize(Workspace workspace) =>
        JsonSerializer.Serialize(new WorkspaceDocument(CurrentSchemaVersion, workspace), Options);

    /// <summary>Never silently returns a default/empty Workspace for bad input - malformed JSON,
    /// a missing/non-numeric schemaVersion, or an unsupported version all throw
    /// WorkspaceSchemaException with a specific, actionable message, rather than quietly
    /// replacing whatever the caller already had.</summary>
    public static Workspace Deserialize(string json)
    {
        using JsonDocument parsed = ParseOrThrow(json);

        if (!parsed.RootElement.TryGetProperty("schemaVersion", out var versionElement) || versionElement.ValueKind != JsonValueKind.Number)
            throw new WorkspaceSchemaException("Malformed workspace document - missing or invalid schemaVersion.");

        int schemaVersion = versionElement.GetInt32();
        string migratedJson = WorkspaceMigration.MigrateToCurrentIfNeeded(json, schemaVersion);

        WorkspaceDocument? document;
        try { document = JsonSerializer.Deserialize<WorkspaceDocument>(migratedJson, Options); }
        catch (JsonException ex) { throw new WorkspaceSchemaException("Malformed workspace document - could not deserialize.", ex); }

        if (document?.Workspace is null)
            throw new WorkspaceSchemaException("Malformed workspace document - missing workspace payload.");

        return document.Workspace;
    }

    private static JsonDocument ParseOrThrow(string json)
    {
        try { return JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new WorkspaceSchemaException("Malformed workspace document - not valid JSON.", ex); }
    }

    /// <summary>Deep-clones a Workspace via a serialize/deserialize round-trip - reuses this same
    /// correctness-tested shape rather than hand-writing a second copy routine. Ids are NOT
    /// regenerated here (a clone is aliased-by-value only) - WorkspaceLayoutService.
    /// DuplicateWorkspace calls this and then explicitly regenerates every Id.</summary>
    public static Workspace Clone(Workspace workspace) => Deserialize(Serialize(workspace));
}

/// <summary>The explicit, narrow migration boundary WorkspaceSerializer.Deserialize consults
/// before deserializing. v1 is the only schema that has ever shipped, so this currently just
/// gates on version equality - the extension point for a real v1-to-v2 upgrade function is the
/// comment below, not a framework that doesn't have a second version to prove itself against yet.</summary>
public static class WorkspaceMigration
{
    public static string MigrateToCurrentIfNeeded(string json, int schemaVersion)
    {
        if (schemaVersion == WorkspaceSerializer.CurrentSchemaVersion) return json;

        if (schemaVersion > WorkspaceSerializer.CurrentSchemaVersion)
            throw new WorkspaceSchemaException(
                $"Workspace schema version {schemaVersion} is newer than this build supports (current: {WorkspaceSerializer.CurrentSchemaVersion}).");

        // No older schema version has ever shipped yet. When v2 lands, this becomes:
        //   if (schemaVersion == 1) json = UpgradeV1ToV2(json);
        //   return json;
        throw new WorkspaceSchemaException($"Workspace schema version {schemaVersion} is not supported by this build.");
    }
}
