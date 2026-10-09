const listeners = new WeakMap();
const arrowControls = 'input, textarea, select, [contenteditable]:not([contenteditable="false"]), ' +
    '[role="textbox"], [role="combobox"], [role="listbox"], [role="slider"], [role="spinbutton"], ' +
    '[role="tablist"], [role="tree"], [role="menu"], [role="menubar"], [role="radiogroup"], [data-arrow-navigation="native"]';

export function attach(root, reference) {
    detach(root);
    const keydown = event => {
        if (!root.isConnected || event.defaultPrevented || event.isComposing || event.repeat ||
            event.altKey || event.ctrlKey || event.metaKey || event.shiftKey ||
            (event.key !== "ArrowLeft" && event.key !== "ArrowRight")) return;
        const target = event.target;
        if (!(target instanceof Element) || target.closest(arrowControls) ||
            target.closest("dialog") || document.querySelector("dialog:modal") ||
            (target !== document.body && target !== document.documentElement && !root.contains(target))) return;
        event.preventDefault();
        if (root.dataset.keyboardBusy) return;
        root.dataset.keyboardBusy = "true";
        reference.invokeMethodAsync("NavigateByKeyboardAsync", event.key === "ArrowLeft" ? -1 : 1)
            .finally(() => { delete root.dataset.keyboardBusy; });
    };
    document.addEventListener("keydown", keydown);
    listeners.set(root, keydown);
    root.dataset.keyboardReady = "true";
}

export function restoreFocus(root, heading) {
    const focused = document.activeElement;
    if (!focused || focused === document.body || focused.matches(":disabled") ||
        focused.closest("[hidden], [inert], [aria-hidden=\"true\"]")) {
        if (root.isConnected) heading.focus({ preventScroll: true });
    }
}

export function detach(root) {
    const keydown = listeners.get(root);
    if (!keydown) return;
    document.removeEventListener("keydown", keydown);
    listeners.delete(root);
    delete root.dataset.keyboardReady;
}