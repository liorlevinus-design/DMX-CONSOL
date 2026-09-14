using System.Text.Json.Serialization;

namespace DmxConsole.Web.Workspaces;

public enum SplitOrientation { Horizontal, Vertical }

/// <summary>One node in a Workspace Surface's pane tree - either a SplitNode (two children) or a
/// TabPaneNode (a strip of hosted Views). Tree surgery (splitting, closing, promoting a sibling,
/// keeping ActiveTabId valid) lives entirely in WorkspaceLayoutService, never in Razor - see its
/// own doc comment for why. [JsonPolymorphic]/[JsonDerivedType] give System.Text.Json enough
/// information to round-trip this abstract hierarchy without a hand-written converter.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(SplitNode), "split")]
[JsonDerivedType(typeof(TabPaneNode), "tabs")]
public abstract class PaneNode
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

public sealed class SplitNode : PaneNode
{
    public SplitOrientation Orientation { get; set; }

    /// <summary>Share of the space given to ChildA, 0..1.</summary>
    public double Ratio { get; set; } = 0.5;

    public PaneNode ChildA { get; set; } = null!;
    public PaneNode ChildB { get; set; } = null!;
}

public sealed class TabPaneNode : PaneNode
{
    public List<ViewInstance> Tabs { get; set; } = new();

    /// <summary>Guid.Empty when there are no tabs. WorkspaceLayoutService is responsible for
    /// keeping this pointed at a real entry in Tabs whenever Tabs is non-empty.</summary>
    public Guid ActiveTabId { get; set; }

    public bool Maximized { get; set; }
}
