namespace DmxConsole.Web.Workspaces;

/// <summary>One hosted instance of a View, living as a tab within a TabPaneNode. Id is stable
/// across Workspace save/load - survives rename, moving between panes, everything. Config is a
/// free-form per-instance settings bag for this milestone (e.g. "which CueList"); a future
/// typed sub-model (like a Stage Layout's own object placements) can live alongside this without
/// changing the shape - Stage Layout content is state a StageLayout-kind ViewInstance owns, never
/// part of the Workspace/PaneNode tree itself (Workspace Layout and Stage Layout are deliberately
/// separate models).</summary>
public sealed class ViewInstance
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public ViewKind Kind { get; init; }
    public string Title { get; set; } = "";
    public Dictionary<string, string> Config { get; set; } = new();
}
