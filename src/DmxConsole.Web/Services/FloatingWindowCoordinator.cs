namespace DmxConsole.Web.Services;

/// <summary>Position/z-order/visibility for ONE floating operator window - purely positional
/// state, never business/command state (no Selection, Programmer, Playback or CommandComposer
/// reference of any kind belongs here). Owned by <see cref="FloatingWindowCoordinator"/>; a
/// <c>FloatingWindow.razor</c> instance reads/writes it but never holds its own copy.</summary>
public sealed class FloatingWindowState
{
    public required string Id { get; init; }
    public double X { get; set; }
    public double Y { get; set; }
    public int ZIndex { get; set; }
    public bool IsVisible { get; set; } = true;

    /// <summary>The position this window was first registered with. Used as the known-safe
    /// anchor by <see cref="FloatingWindowCoordinator.Recover"/> - never mutated after
    /// registration, so recovery is always deterministic regardless of how far the window has
    /// since been dragged.</summary>
    public required double DefaultX { get; init; }
    public required double DefaultY { get; init; }
}

/// <summary>Tracks per-window position, z-order and visibility for every floating operator
/// surface (Command Surface today; any future floatable panel registers here too, rather than
/// growing its own drag/z-order code - see FloatingWindow.razor's doc comment). Deliberately
/// in-memory only for this slice: positions reset on reload/reconnect. Persisting them into the
/// Workspace's saved JSON is explicit future work, not implemented here - extending that schema
/// is out of scope for this change.
///
/// Registered per-circuit (Scoped in Program.cs), unlike the shared/Singleton MainViewModel -
/// "where my windows are on screen" is this operator's own client-local UI preference, not
/// console show state broadcast to every connected tablet.</summary>
public sealed class FloatingWindowCoordinator
{
    /// <summary>How much of a window's top-left corner (where the draggable header/title bar
    /// starts) must always stay within the viewport. This is the "usable title/header recovery
    /// region" the positioning-bug fix requires - a window may still have most of its body run
    /// off-screen for a large window, but its origin corner, and therefore its header, is always
    /// reachable so the operator can always grab and drag it back. Sized to the same touch-target
    /// convention already used elsewhere in the shell (CLAUDE.md's 44px minimum hit target).</summary>
    private const double MinVisibleMarginPx = 48;

    /// <summary>Fallback viewport size used before the shell has reported a real one (e.g. before
    /// first render's JS interop round-trip completes). Deliberately generous so nothing clamps
    /// unexpectedly tight on startup; <see cref="SetViewportSize"/> replaces it with the real,
    /// current browser viewport as soon as it's known, and re-clamps every window against it.</summary>
    private double _viewportWidth = 1920;
    private double _viewportHeight = 1080;

    private readonly Dictionary<string, FloatingWindowState> _windows = new();
    private int _nextZIndex = 1;

    /// <summary>Raised after any registered window's position, z-order or visibility changes.
    /// Never raised as a side effect of anything outside this coordinator's own state.</summary>
    public event Action? Changed;

    /// <summary>Returns the existing state for <paramref name="id"/>, or creates and registers
    /// one (already brought to front) using the supplied defaults. Idempotent per id - a second
    /// call with different defaults is ignored once the window already exists.</summary>
    public FloatingWindowState GetOrRegister(string id, double defaultX, double defaultY)
    {
        if (_windows.TryGetValue(id, out var existing)) return existing;

        var (x, y) = Clamp(defaultX, defaultY);
        var state = new FloatingWindowState
        {
            Id = id, X = x, Y = y, ZIndex = _nextZIndex++,
            DefaultX = defaultX, DefaultY = defaultY,
        };
        _windows[id] = state;
        return state;
    }

    public FloatingWindowState? Find(string id) => _windows.GetValueOrDefault(id);

    /// <summary>Moves a window to an absolute position, clamped so its header/origin corner
    /// always stays within the current viewport (see <see cref="MinVisibleMarginPx"/>) - a
    /// window can never be dragged fully off-screen and become unrecoverable. No-ops silently
    /// for an unknown id - callers never need to guard on registration order.</summary>
    public void Move(string id, double x, double y)
    {
        if (!_windows.TryGetValue(id, out var state)) return;
        (state.X, state.Y) = Clamp(x, y);
        Changed?.Invoke();
    }

    /// <summary>Reports the current browser viewport size (called by the shell after an initial
    /// JS interop round-trip and on every subsequent resize). Re-clamps every already-registered
    /// window against the new size, so shrinking the viewport can never strand a window that was
    /// reachable a moment ago - this is the automatic-recovery-on-resize half of the fix. Ignores
    /// non-positive sizes (e.g. a transient 0x0 during a reload) rather than clamping everything
    /// into a corner.</summary>
    public void SetViewportSize(double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        _viewportWidth = width;
        _viewportHeight = height;

        var changed = false;
        foreach (var state in _windows.Values)
        {
            var (x, y) = Clamp(state.X, state.Y);
            if (x == state.X && y == state.Y) continue;
            state.X = x;
            state.Y = y;
            changed = true;
        }

        if (changed) Changed?.Invoke();
    }

    /// <summary>Guaranteed recovery path for a window that has become hard to find - whether it
    /// was dragged off-screen, or simply hidden. Makes it visible, resets it to its own known-safe
    /// registration anchor (re-clamped against the current viewport, in case that anchor predates
    /// a later resize), and brings it to front. Always produces a reachable window; no-ops
    /// silently for an unknown id.</summary>
    public void Recover(string id)
    {
        if (!_windows.TryGetValue(id, out var state)) return;

        (state.X, state.Y) = Clamp(state.DefaultX, state.DefaultY);
        state.IsVisible = true;
        state.ZIndex = _nextZIndex++;
        Changed?.Invoke();
    }

    private (double X, double Y) Clamp(double x, double y)
    {
        var maxX = Math.Max(0, _viewportWidth - MinVisibleMarginPx);
        var maxY = Math.Max(0, _viewportHeight - MinVisibleMarginPx);
        return (Math.Clamp(x, 0, maxX), Math.Clamp(y, 0, maxY));
    }

    /// <summary>Gives <paramref name="id"/> the highest z-index of any tracked window, so it
    /// draws above every other overlapping floating window.</summary>
    public void BringToFront(string id)
    {
        if (!_windows.TryGetValue(id, out var state)) return;
        state.ZIndex = _nextZIndex++;
        Changed?.Invoke();
    }

    public void Show(string id)
    {
        if (!_windows.TryGetValue(id, out var state) || state.IsVisible) return;
        state.IsVisible = true;
        state.ZIndex = _nextZIndex++;
        Changed?.Invoke();
    }

    public void Hide(string id)
    {
        if (!_windows.TryGetValue(id, out var state) || !state.IsVisible) return;
        state.IsVisible = false;
        Changed?.Invoke();
    }

    public void Toggle(string id)
    {
        var state = Find(id);
        if (state is null) return;
        if (state.IsVisible) Hide(id); else Show(id);
    }
}
