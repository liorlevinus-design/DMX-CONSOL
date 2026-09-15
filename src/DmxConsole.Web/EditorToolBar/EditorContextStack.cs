namespace DmxConsole.Web.EditorToolBar;

/// <summary>
/// The ONE shared operator context - H1.6 §19 is explicit that there must not be a separate GUI
/// context and Command Surface context. A View's row click and CommandSurfaceViewModel's token
/// handling both call the same EnterObject/Push/Back/Root here; EditorToolBar.razor is a pure
/// presentation layer over it, nothing more (§19's own wording: "only a presentation and
/// interaction layer over that shared context").
///
/// UI-independent and framework-free by design (§7/§16) - constructible and testable with zero
/// Blazor dependency.
/// </summary>
public sealed class EditorContextStack
{
    public static readonly EditorContextFrame RootFrame = new() { Id = "Root", Label = "ROOT" };

    private readonly List<EditorContextFrame> _frames = new();

    /// <summary>The full breadcrumb path, root-first. Empty means "at Root".</summary>
    public IReadOnlyList<EditorContextFrame> Frames => _frames;

    public EditorContextFrame Current => _frames.Count > 0 ? _frames[^1] : RootFrame;

    public bool IsAtRoot => _frames.Count == 0;

    public event Action? Changed;

    /// <summary>Enters a top-level object family (FIXTURE/GROUP/CUE/...), always REPLACING
    /// whatever context path existed before - selecting a new object starts a fresh path, never
    /// nests under an unrelated previous one (matches how clicking a different Cue while already
    /// in CUE &gt; TIME snaps back to a flat CUE context for the newly-clicked one, not
    /// CUE &gt; TIME for the old selection).</summary>
    public void EnterObject(EditorObjectType objectType, string label, object? selectedObject = null, string selectedObjectLabel = "")
    {
        _frames.Clear();
        _frames.Add(new EditorContextFrame
        {
            Id = objectType.ToString(),
            Label = label,
            ObjectType = objectType,
            SelectedObject = selectedObject,
            SelectedObjectLabel = selectedObjectLabel,
        });
        Changed?.Invoke();
    }

    /// <summary>Pushes a deeper sub-context under the current one (CUE -&gt; CUE.TIME). No-op at
    /// Root - there is nothing to nest a sub-context under yet.</summary>
    public void Push(string idSegment, string label)
    {
        if (_frames.Count == 0) return;

        var parent = _frames[^1];
        _frames.Add(parent with { Id = $"{parent.Id}.{idSegment}", Label = label });
        Changed?.Invoke();
    }

    /// <summary>One level back. Popping the last frame returns to Root.</summary>
    public void Back()
    {
        if (_frames.Count == 0) return;
        _frames.RemoveAt(_frames.Count - 1);
        Changed?.Invoke();
    }

    public void Root()
    {
        if (_frames.Count == 0) return;
        _frames.Clear();
        Changed?.Invoke();
    }
}
