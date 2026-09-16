// Small, dependency-free pointer-capture helpers for EncoderKnob.razor's rotary-drag
// interaction - same shape as workspacePaneResize.js's dmxPaneResize (not reused directly, to
// avoid coupling the already-working pane-resize drag to a differently-named feature). Pointer
// capture keeps pointermove/pointerup arriving at the knob even when a fast vertical drag
// leaves its small circular hit area mid-gesture.
window.dmxEncoderKnob = {
    capture: function (el, pointerId) {
        try { el.setPointerCapture(pointerId); } catch (e) { /* pointer already gone - ignore */ }
    },
    release: function (el, pointerId) {
        try { el.releasePointerCapture(pointerId); } catch (e) { /* already released - ignore */ }
    },
    getRect: function (el) {
        const rect = el.getBoundingClientRect();
        return { top: rect.top, left: rect.left, width: rect.width, height: rect.height };
    }
};
