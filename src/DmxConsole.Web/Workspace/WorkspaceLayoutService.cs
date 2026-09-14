namespace DmxConsole.Web.Workspaces;

/// <summary>
/// The single owner of every Workspace/Pane tree mutation - Split/Close/Add/Move/Resize/
/// Maximize/Lock/Rename/Duplicate. PaneHost.razor (and, later, keyboard shortcuts, the command
/// line, remote clients, or an NL layer) only ever collects user intent and calls here - tree
/// surgery (replacing a pane with a SplitNode, collapsing a SplitNode when a child disappears,
/// promoting the remaining sibling, keeping ActiveTabId valid) lives exactly once, in this
/// tested service, so every input method gets identical, correct behavior for free.
///
/// Every structural method returns false (never throws) on an invalid target or a locked
/// Workspace - "no silent guessing, no crash" is used throughout this project's Core/Application
/// layers and applies here too. SetActiveTab is deliberately NOT gated by LayoutLocked - viewing
/// a different tab is normal interaction, not a structural layout edit.
/// </summary>
public sealed class WorkspaceLayoutService
{
    public bool SplitPane(Workspace workspace, WorkspaceSurface surface, Guid paneId, SplitOrientation orientation)
    {
        if (workspace.LayoutLocked) return false;

        var target = FindNode(surface.Root, paneId);
        if (target is null) return false;

        var split = new SplitNode { Orientation = orientation, ChildA = target, ChildB = new TabPaneNode() };
        return ReplaceNode(surface, paneId, split);
    }

    /// <summary>Closes an entire pane (a TabPaneNode and every tab in it, or a whole sub-tree).
    /// The sibling under the same parent SplitNode is promoted into the parent's place. Closing
    /// the Surface's Root pane leaves a fresh, empty TabPaneNode as the new Root - Root is never
    /// null.</summary>
    public bool ClosePane(Workspace workspace, WorkspaceSurface surface, Guid paneId)
    {
        if (workspace.LayoutLocked) return false;
        return ClosePaneInternal(surface, paneId);
    }

    private static bool ClosePaneInternal(WorkspaceSurface surface, Guid paneId)
    {
        if (surface.Root.Id == paneId)
        {
            surface.Root = new TabPaneNode();
            return true;
        }

        var parent = FindParentSplit(surface.Root, paneId);
        if (parent is null) return false;

        var sibling = parent.ChildA.Id == paneId ? parent.ChildB : parent.ChildA;
        return ReplaceNode(surface, parent.Id, sibling);
    }

    public bool AddView(Workspace workspace, WorkspaceSurface surface, Guid tabPaneId, ViewInstance view)
    {
        if (workspace.LayoutLocked) return false;
        if (FindNode(surface.Root, tabPaneId) is not TabPaneNode tabPane) return false;

        tabPane.Tabs.Add(view);
        tabPane.ActiveTabId = view.Id;
        return true;
    }

    /// <summary>Removes one tab. If other tabs remain, ActiveTabId is kept valid (moved to the
    /// first remaining tab if the closed one was active). If it was the last tab, the whole pane
    /// collapses via ClosePaneInternal - promoting its sibling, exactly like ClosePane.</summary>
    public bool CloseView(Workspace workspace, WorkspaceSurface surface, Guid viewInstanceId)
    {
        if (workspace.LayoutLocked) return false;

        var tabPane = FindTabPaneContaining(surface.Root, viewInstanceId);
        var view = tabPane?.Tabs.FirstOrDefault(t => t.Id == viewInstanceId);
        if (tabPane is null || view is null) return false;

        tabPane.Tabs.Remove(view);

        if (tabPane.Tabs.Count == 0)
        {
            tabPane.ActiveTabId = Guid.Empty;
            return ClosePaneInternal(surface, tabPane.Id);
        }

        if (tabPane.ActiveTabId == viewInstanceId)
            tabPane.ActiveTabId = tabPane.Tabs[0].Id;

        return true;
    }

    public bool SetActiveTab(WorkspaceSurface surface, Guid tabPaneId, Guid viewInstanceId)
    {
        if (FindNode(surface.Root, tabPaneId) is not TabPaneNode tabPane) return false;
        if (!tabPane.Tabs.Any(t => t.Id == viewInstanceId)) return false;

        tabPane.ActiveTabId = viewInstanceId;
        return true;
    }

    /// <summary>Moves one View from wherever it currently is to a different TabPaneNode. If it
    /// was the last tab in its old pane, that pane collapses exactly like CloseView does.</summary>
    public bool MoveView(Workspace workspace, WorkspaceSurface surface, Guid viewInstanceId, Guid targetTabPaneId)
    {
        if (workspace.LayoutLocked) return false;

        var sourceTabPane = FindTabPaneContaining(surface.Root, viewInstanceId);
        var view = sourceTabPane?.Tabs.FirstOrDefault(t => t.Id == viewInstanceId);
        if (sourceTabPane is null || view is null) return false;
        if (FindNode(surface.Root, targetTabPaneId) is not TabPaneNode targetTabPane) return false;
        if (ReferenceEquals(sourceTabPane, targetTabPane)) return true; // no-op, already there

        sourceTabPane.Tabs.Remove(view);
        targetTabPane.Tabs.Add(view);
        targetTabPane.ActiveTabId = view.Id;

        if (sourceTabPane.Tabs.Count == 0)
        {
            sourceTabPane.ActiveTabId = Guid.Empty;
            ClosePaneInternal(surface, sourceTabPane.Id);
        }
        else if (sourceTabPane.ActiveTabId == viewInstanceId)
        {
            sourceTabPane.ActiveTabId = sourceTabPane.Tabs[0].Id;
        }

        return true;
    }

    public bool ResizeSplit(Workspace workspace, WorkspaceSurface surface, Guid splitNodeId, double ratio)
    {
        if (workspace.LayoutLocked) return false;
        if (FindNode(surface.Root, splitNodeId) is not SplitNode split) return false;

        split.Ratio = Math.Clamp(ratio, 0.05, 0.95);
        return true;
    }

    public bool MaximizePane(Workspace workspace, WorkspaceSurface surface, Guid paneId)
    {
        if (workspace.LayoutLocked) return false;
        if (FindNode(surface.Root, paneId) is not TabPaneNode tabPane) return false;

        ForEachTabPane(surface.Root, t => t.Maximized = false); // only one maximized pane at a time
        tabPane.Maximized = true;
        return true;
    }

    public bool RestorePane(Workspace workspace, WorkspaceSurface surface)
    {
        if (workspace.LayoutLocked) return false;
        ForEachTabPane(surface.Root, t => t.Maximized = false);
        return true;
    }

    public void LockLayout(Workspace workspace) => workspace.LayoutLocked = true;
    public void UnlockLayout(Workspace workspace) => workspace.LayoutLocked = false;
    public void RenameWorkspace(Workspace workspace, string newName) => workspace.Name = newName;

    /// <summary>A genuinely distinct copy - new Ids throughout (Workspace/Surfaces/every
    /// PaneNode/every ViewInstance), always User-scoped regardless of the source's scope (this is
    /// exactly how a Factory workspace becomes editable: Duplicate it, then edit the copy - the
    /// factory original is never mutated in place).</summary>
    public Workspace DuplicateWorkspace(Workspace source)
    {
        var clone = WorkspaceSerializer.Clone(source);
        RegenerateIds(clone);
        clone.Scope = WorkspaceScope.User;
        clone.Name = source.Name + " Copy";
        return clone;
    }

    private static void RegenerateIds(Workspace workspace)
    {
        SetId(workspace, Guid.NewGuid());
        foreach (var surface in workspace.Surfaces)
        {
            SetId(surface, Guid.NewGuid());
            RegenerateNodeIds(surface.Root);
        }
    }

    private static void RegenerateNodeIds(PaneNode node)
    {
        SetId(node, Guid.NewGuid());
        switch (node)
        {
            case SplitNode split:
                RegenerateNodeIds(split.ChildA);
                RegenerateNodeIds(split.ChildB);
                break;
            case TabPaneNode tabPane:
                var oldToNew = new Dictionary<Guid, Guid>();
                foreach (var view in tabPane.Tabs)
                {
                    var newId = Guid.NewGuid();
                    oldToNew[view.Id] = newId;
                    SetId(view, newId);
                }
                if (oldToNew.TryGetValue(tabPane.ActiveTabId, out var newActive)) tabPane.ActiveTabId = newActive;
                break;
        }
    }

    // Id has an `init` setter - reflection is the simplest way to regenerate it post-construction
    // for a deep clone, confined to this one private helper rather than opening the setter publicly.
    private static void SetId(object target, Guid newId)
    {
        var property = target.GetType().GetProperty("Id") ?? throw new InvalidOperationException($"{target.GetType()} has no Id property.");
        property.SetValue(target, newId);
    }

    private static PaneNode? FindNode(PaneNode node, Guid id)
    {
        if (node.Id == id) return node;
        if (node is SplitNode split) return FindNode(split.ChildA, id) ?? FindNode(split.ChildB, id);
        return null;
    }

    private static SplitNode? FindParentSplit(PaneNode node, Guid childId)
    {
        if (node is not SplitNode split) return null;
        if (split.ChildA.Id == childId || split.ChildB.Id == childId) return split;
        return FindParentSplit(split.ChildA, childId) ?? FindParentSplit(split.ChildB, childId);
    }

    private static TabPaneNode? FindTabPaneContaining(PaneNode node, Guid viewInstanceId)
    {
        if (node is TabPaneNode tabPane) return tabPane.Tabs.Any(t => t.Id == viewInstanceId) ? tabPane : null;
        if (node is SplitNode split) return FindTabPaneContaining(split.ChildA, viewInstanceId) ?? FindTabPaneContaining(split.ChildB, viewInstanceId);
        return null;
    }

    private static void ForEachTabPane(PaneNode node, Action<TabPaneNode> action)
    {
        if (node is TabPaneNode tabPane) { action(tabPane); return; }
        if (node is SplitNode split) { ForEachTabPane(split.ChildA, action); ForEachTabPane(split.ChildB, action); }
    }

    private static bool ReplaceNode(WorkspaceSurface surface, Guid targetId, PaneNode replacement)
    {
        if (surface.Root.Id == targetId) { surface.Root = replacement; return true; }
        return ReplaceInSubtree(surface.Root, targetId, replacement);
    }

    private static bool ReplaceInSubtree(PaneNode node, Guid targetId, PaneNode replacement)
    {
        if (node is not SplitNode split) return false;
        if (split.ChildA.Id == targetId) { split.ChildA = replacement; return true; }
        if (split.ChildB.Id == targetId) { split.ChildB = replacement; return true; }
        return ReplaceInSubtree(split.ChildA, targetId, replacement) || ReplaceInSubtree(split.ChildB, targetId, replacement);
    }
}
