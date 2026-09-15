namespace DmxConsole.Web.EditorToolBar;

/// <summary>The set of soft keys this build knows how to show for each context id - a narrow,
/// explicit registry (not reflection/convention-based), same discipline as ViewRegistry. Never
/// throws for an unregistered context id - an object family that doesn't have a tree built yet
/// (H1.6 §4's Preset/Executor/Submaster/Effect/Macro/Patch/Programmer, reserved for later slices)
/// simply renders an empty tool bar, not an error.</summary>
public sealed class SoftKeyRegistry
{
    private readonly Dictionary<string, List<SoftKeyDefinition>> _byContext = new();

    public void Register(SoftKeyDefinition key)
    {
        if (!_byContext.TryGetValue(key.ContextId, out var list))
            _byContext[key.ContextId] = list = new List<SoftKeyDefinition>();

        list.Add(key);
    }

    public void RegisterRange(IEnumerable<SoftKeyDefinition> keys)
    {
        foreach (var key in keys) Register(key);
    }

    public IReadOnlyList<SoftKeyDefinition> For(string contextId) =>
        _byContext.TryGetValue(contextId, out var list) ? list : Array.Empty<SoftKeyDefinition>();
}
