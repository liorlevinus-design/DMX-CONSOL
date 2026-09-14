using DmxConsole.Web.Components.ConsoleUi;
using DmxConsole.Web.Workspaces;

namespace DmxConsole.Web.Services;

/// <summary>
/// The shared Workspace shell state - one instance for the whole app (Singleton, same pattern
/// as MainViewModel: every connected browser/tablet looks at the same live layout, exactly like
/// a real console with multiple control surfaces). Holds the ViewRegistry (what View kinds this
/// build knows how to host), the WorkspaceLayoutService (pure tree-mutation logic, no UI
/// dependency), the persisted list of Workspaces (one built-in Factory one plus whatever
/// LocalJsonWorkspaceStore has on disk), and which one is currently active.
/// </summary>
public sealed class WorkspaceViewModel
{
    private readonly IWorkspaceStore _store;
    private readonly List<Workspace> _workspaces = new();
    private bool _initialized;

    public ViewRegistry Registry { get; } = new();
    public WorkspaceLayoutService Layout { get; } = new();
    public Workspace Current { get; private set; }
    public IReadOnlyList<Workspace> Workspaces => _workspaces;

    public WorkspaceViewModel() : this(new LocalJsonWorkspaceStore()) { }

    public WorkspaceViewModel(IWorkspaceStore store)
    {
        _store = store;
        RegisterViews();
        var factory = BuildDefaultWorkspace();
        _workspaces.Add(factory);
        Current = factory;
    }

    /// <summary>Loads any previously-saved User workspaces from disk and appends them after the
    /// built-in Factory one. Safe to call more than once (only the first call does anything) -
    /// callers (WorkspacePreview.razor's OnInitializedAsync) don't need to track this themselves.
    /// Not done in the constructor because file I/O has no place in a DI container's synchronous
    /// object-graph construction.</summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        var loaded = await _store.LoadAllAsync();
        _workspaces.AddRange(loaded);
    }

    /// <summary>Phase H1: every existing fixed-tab console component becomes hostable by the
    /// Workspace shell, unchanged - proving the architecture over each real screen, not
    /// redesigning them yet (that's a later, separate step per-View). ViewKind.Fixtures has no
    /// existing component to wrap - nothing was ever built standalone for it (the old fixed UI
    /// never had a dedicated Fixtures screen either) - so it stays unregistered until Phase H1's
    /// own "Fixtures View" step builds one; StageLayout/ThreeD/TrackSheet/Diagnostics stay
    /// unregistered too, per the milestone's "keep as placeholders for later" instruction.</summary>
    private void RegisterViews()
    {
        Registry.Register(new ViewDescriptor(ViewKind.Channels, "Channels", typeof(ChannelsView)));
        Registry.Register(new ViewDescriptor(ViewKind.Patch, "Patch", typeof(PatchPanel)));
        Registry.Register(new ViewDescriptor(ViewKind.CueList, "Cues", typeof(CueListPanel)));
        Registry.Register(new ViewDescriptor(ViewKind.Executors, "Executors", typeof(ExecutorPanel)));
        Registry.Register(new ViewDescriptor(ViewKind.Presets, "Presets", typeof(PresetPanel)));
        Registry.Register(new ViewDescriptor(ViewKind.Effects, "Effects", typeof(EffectsPanel)));
        Registry.Register(new ViewDescriptor(ViewKind.Programmer, "Programmer", typeof(ProgrammerPanel)));
    }

    /// <summary>The real factory layout for day-to-day programming (not just a compatibility
    /// clone of the old fixed tab strip): Channels+Patch and Programmer+Presets on the left,
    /// Cue List and Executors+Effects on the right. Purely a starting point - the operator can
    /// rearrange/duplicate/save over it like any other Workspace (see WorkspaceLayoutService).</summary>
    private static Workspace BuildDefaultWorkspace()
    {
        var channelsTab = new ViewInstance { Kind = ViewKind.Channels, Title = "Channels" };
        var patchTab = new ViewInstance { Kind = ViewKind.Patch, Title = "Patch" };
        var topLeft = new TabPaneNode { Tabs = { channelsTab, patchTab }, ActiveTabId = channelsTab.Id };

        var programmerTab = new ViewInstance { Kind = ViewKind.Programmer, Title = "Programmer" };
        var presetsTab = new ViewInstance { Kind = ViewKind.Presets, Title = "Presets" };
        var bottomLeft = new TabPaneNode { Tabs = { programmerTab, presetsTab }, ActiveTabId = programmerTab.Id };

        var cueListTab = new ViewInstance { Kind = ViewKind.CueList, Title = "Cues" };
        var topRight = new TabPaneNode { Tabs = { cueListTab }, ActiveTabId = cueListTab.Id };

        var executorsTab = new ViewInstance { Kind = ViewKind.Executors, Title = "Executors" };
        var effectsTab = new ViewInstance { Kind = ViewKind.Effects, Title = "Effects" };
        var bottomRight = new TabPaneNode { Tabs = { executorsTab, effectsTab }, ActiveTabId = executorsTab.Id };

        var leftColumn = new SplitNode { Orientation = SplitOrientation.Vertical, Ratio = 0.5, ChildA = topLeft, ChildB = bottomLeft };
        var rightColumn = new SplitNode { Orientation = SplitOrientation.Vertical, Ratio = 0.5, ChildA = topRight, ChildB = bottomRight };
        var root = new SplitNode { Orientation = SplitOrientation.Horizontal, Ratio = 0.5, ChildA = leftColumn, ChildB = rightColumn };

        var surface = new WorkspaceSurface { Name = "Main", Root = root };
        return new Workspace { Name = "Default", Scope = WorkspaceScope.Factory, Surfaces = { surface } };
    }

    public void SwitchTo(Guid workspaceId)
    {
        var target = _workspaces.FirstOrDefault(w => w.Id == workspaceId);
        if (target is not null) Current = target;
    }

    /// <summary>A fresh, empty User workspace - added to the list and made active, but not
    /// persisted until an explicit Save (consistent with every other "New" in this app never
    /// touching disk on its own).</summary>
    public void NewWorkspace()
    {
        var surface = new WorkspaceSurface { Name = "Main", Root = new TabPaneNode() };
        var workspace = new Workspace { Name = "New Workspace", Scope = WorkspaceScope.User, Surfaces = { surface } };
        _workspaces.Add(workspace);
        Current = workspace;
    }

    /// <summary>Saves the current Workspace. A Factory-scoped one is never written in place -
    /// this implicitly Duplicates it to a new User-scoped copy first (structural, not a rule a
    /// caller has to remember to apply) and makes THAT the active/current workspace.</summary>
    public async Task SaveAsync()
    {
        if (Current.Scope == WorkspaceScope.Factory)
        {
            var copy = Layout.DuplicateWorkspace(Current);
            _workspaces.Add(copy);
            Current = copy;
        }

        await _store.SaveAsync(Current);
    }

    public async Task SaveAsAsync(string newName)
    {
        var copy = Layout.DuplicateWorkspace(Current);
        copy.Name = newName;
        _workspaces.Add(copy);
        Current = copy;
        await _store.SaveAsync(copy);
    }

    /// <summary>No-op (false) on a Factory workspace - renaming the built-in default in place
    /// isn't offered; Save/SaveAs/Duplicate are how it becomes an editable, renameable copy.</summary>
    public bool Rename(string newName)
    {
        if (Current.Scope == WorkspaceScope.Factory) return false;
        Layout.RenameWorkspace(Current, newName);
        return true;
    }

    public void Duplicate()
    {
        var copy = Layout.DuplicateWorkspace(Current);
        _workspaces.Add(copy);
        Current = copy;
    }

    /// <summary>No-op (false) on a Factory workspace - it can't be deleted, only duplicated into
    /// an editable copy. Deleting the active workspace switches Current to the Factory one (the
    /// list's first entry is always the Factory workspace by construction).</summary>
    public async Task<bool> DeleteAsync(Guid workspaceId)
    {
        var target = _workspaces.FirstOrDefault(w => w.Id == workspaceId);
        if (target is null || target.Scope == WorkspaceScope.Factory) return false;

        _workspaces.Remove(target);
        await _store.DeleteAsync(workspaceId);

        if (Current.Id == workspaceId) Current = _workspaces[0];
        return true;
    }

    /// <summary>Session-lived display order only (not persisted - no saved-layout schema change
    /// needed for this). Swaps the workspace at index with its neighbor. The Factory workspace
    /// stays pinned at index 0 (it's never movable, and nothing else can be swapped into its
    /// slot) - DeleteAsync relies on index 0 always being the Factory fallback.</summary>
    public void MoveWorkspace(Guid workspaceId, int direction)
    {
        int index = _workspaces.FindIndex(w => w.Id == workspaceId);
        int targetIndex = index + direction;
        if (index <= 0 || targetIndex <= 0 || targetIndex >= _workspaces.Count) return;

        (_workspaces[index], _workspaces[targetIndex]) = (_workspaces[targetIndex], _workspaces[index]);
    }
}
