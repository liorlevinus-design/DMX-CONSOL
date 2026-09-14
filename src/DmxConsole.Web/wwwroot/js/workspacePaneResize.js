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
    }
};
