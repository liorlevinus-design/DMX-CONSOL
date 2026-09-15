namespace DmxConsole.Web.Workspaces;

/// <summary>Every first-class View kind the shell knows how to host. Adding a new kind here (and
/// registering a ViewDescriptor for it) is the only step needed to make it available to every
/// Workspace's Pane tree - no Workspace/PaneNode code change required.</summary>
public enum ViewKind
{
    Channels,
    Fixtures,
    CueList,
    Executors,
    Programmer,
    Effects,
    Patch,
    Presets,
    Groups,
    StageLayout,
    ThreeD,
    TrackSheet,
    Diagnostics,
}
