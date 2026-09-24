// Small, deliberately dependency-free helpers for PaneHost.razor's split-drag interaction.
// Pointer capture is a native DOM capability with no Blazor-managed equivalent - once captured,
// the handle keeps receiving pointermove/pointerup even when the cursor leaves its thin hit area
// mid-drag, which is what makes a fast drag feel correct instead of "sticky"/dropped.
window.dmxPaneResize = {
    capture: function (el, pointerId) {
        try { el.setPointerCapture(pointerId); } catch (e) { /* pointer already gone - ignore */ }
    },
    release: function (el, pointerId) {
        try { el.releasePointerCapture(pointerId); } catch (e) { /* already released - ignore */ }
    },
    getRect: function (el) {
        var r = el.getBoundingClientRect();
        return { width: r.width, height: r.height };
    },

    // Viewport tracking for FloatingWindowCoordinator's clamping (see FloatingWindowCoordinator.cs
    // and ConsoleShell.razor's OnAfterRenderAsync) - reports the browser viewport size once on
    // startup and again on every resize, so a floating window's position can be re-clamped and
    // never left stranded outside a shrunk window. Deliberately generic (viewport size only, no
    // Command-Surface-specific knowledge) so any future floatable panel benefits the same way.
    getViewportSize: function () {
        return { width: window.innerWidth, height: window.innerHeight };
    },
    // watchViewportResize/unwatchViewportResize: paired register/unregister so the caller (today,
    // only ConsoleShell.razor's OnAfterRenderAsync/DisposeAsync) can remove its listener on
    // dispose - otherwise the anonymous closure below would keep firing into an already-disposed
    // DotNetObjectReference after the component goes away, and a second watch (e.g. the component
    // being reconstructed) would pile up a second listener alongside it. Only one caller exists in
    // this app today, so a single module-local slot (rather than a per-dotNetRef map) is enough -
    // watch always clears any previous listener first, so calling it twice without an intervening
    // unwatch still can't double-fire.
    _viewportResizeListener: null,
    watchViewportResize: function (dotNetRef) {
        if (window.dmxPaneResize._viewportResizeListener) {
            window.removeEventListener('resize', window.dmxPaneResize._viewportResizeListener);
        }
        window.dmxPaneResize._viewportResizeListener = function () {
            dotNetRef.invokeMethodAsync('OnViewportResized', window.innerWidth, window.innerHeight);
        };
        window.addEventListener('resize', window.dmxPaneResize._viewportResizeListener);
    },
    unwatchViewportResize: function () {
        if (window.dmxPaneResize._viewportResizeListener) {
            window.removeEventListener('resize', window.dmxPaneResize._viewportResizeListener);
            window.dmxPaneResize._viewportResizeListener = null;
        }
    }
};
