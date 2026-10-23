window.kvTheme = (() => {
    const preferenceKey = "kubernetes-visualizer.theme";
    const systemTheme = window.matchMedia("(prefers-color-scheme: dark)");
    let dotnetReference;
    let systemListener;

    return {
        getPreference: () => localStorage.getItem(preferenceKey) ?? "System",
        setPreference: preference => localStorage.setItem(preferenceKey, preference),
        isSystemDark: () => systemTheme.matches,
        subscribeSystem: reference => {
            dotnetReference = reference;
            systemListener = event => dotnetReference.invokeMethodAsync("OnSystemThemeChanged", event.matches);
            systemTheme.addEventListener("change", systemListener);
        },
        unsubscribeSystem: () => {
            if (systemListener) {
                systemTheme.removeEventListener("change", systemListener);
                systemListener = undefined;
                dotnetReference = undefined;
            }
        }
    };
})();

window.kvUiState = (() => {
    const prefix = "kubernetes-visualizer.ui.";
    return {
        read: key => {
            const value = localStorage.getItem(prefix + key);
            return value ? JSON.parse(value) : null;
        },
        write: (key, value) => localStorage.setItem(prefix + key, JSON.stringify(value))
    };
})();

window.kvClipboard = {
    copy: async text => {
        if (!navigator.clipboard?.writeText) return false;
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            return false;
        }
    }
};

window.kvGraph = (() => {
    const handlers = new WeakMap();

    return {
        initialize: (scrollElement, fullscreenElement, dotnetReference) => {
            if (!scrollElement || handlers.has(scrollElement)) return;
            let drag = null;
            const pointerDown = event => {
                if (event.pointerType !== "mouse" || event.button !== 0
                    || (event.target instanceof Element && event.target.closest("button, a, input, .mud-popover"))) return;
                drag = { pointerId: event.pointerId, x: event.clientX, y: event.clientY,
                    left: scrollElement.scrollLeft, top: scrollElement.scrollTop };
                scrollElement.classList.add("panning");
                scrollElement.setPointerCapture(event.pointerId);
                event.preventDefault();
            };
            const pointerMove = event => {
                if (!drag || drag.pointerId !== event.pointerId) return;
                scrollElement.scrollLeft = drag.left - (event.clientX - drag.x);
                scrollElement.scrollTop = drag.top - (event.clientY - drag.y);
            };
            const pointerUp = event => {
                if (!drag || drag.pointerId !== event.pointerId) return;
                if (scrollElement.hasPointerCapture(event.pointerId)) scrollElement.releasePointerCapture(event.pointerId);
                drag = null;
                scrollElement.classList.remove("panning");
            };
            const fullscreenChange = () => dotnetReference.invokeMethodAsync(
                "OnFullscreenChanged", document.fullscreenElement === fullscreenElement);

            scrollElement.addEventListener("pointerdown", pointerDown);
            scrollElement.addEventListener("pointermove", pointerMove);
            scrollElement.addEventListener("pointerup", pointerUp);
            scrollElement.addEventListener("pointercancel", pointerUp);
            document.addEventListener("fullscreenchange", fullscreenChange);
            handlers.set(scrollElement, { pointerDown, pointerMove, pointerUp, fullscreenChange, fullscreenElement });
        },
        toggleFullscreen: async element => {
            if (!document.fullscreenEnabled) return false;
            if (document.fullscreenElement === element) await document.exitFullscreen();
            else await element.requestFullscreen();
            return document.fullscreenElement === element;
        },
        dispose: scrollElement => {
            const state = handlers.get(scrollElement);
            if (!state) return;
            scrollElement.removeEventListener("pointerdown", state.pointerDown);
            scrollElement.removeEventListener("pointermove", state.pointerMove);
            scrollElement.removeEventListener("pointerup", state.pointerUp);
            scrollElement.removeEventListener("pointercancel", state.pointerUp);
            document.removeEventListener("fullscreenchange", state.fullscreenChange);
            handlers.delete(scrollElement);
        }
    };
})();
