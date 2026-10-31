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

window.kvClickAway = (() => {
    const listeners = new WeakMap();

    return {
        bind: (root, dotnetReference) => {
            if (!root || listeners.has(root)) return;
            const listener = event => {
                const path = event.composedPath?.();
                const inside = path ? path.includes(root) : root.contains(event.target);
                if (!inside) {
                    dotnetReference.invokeMethodAsync("OnClickAway").catch(() => {});
                }
            };
            document.addEventListener("pointerup", listener, true);
            document.addEventListener("focusin", listener, true);
            listeners.set(root, listener);
        },
        unbind: root => {
            const listener = root && listeners.get(root);
            if (!listener) return;
            document.removeEventListener("pointerup", listener, true);
            document.removeEventListener("focusin", listener, true);
            listeners.delete(root);
        }
    };
})();

window.kvResourceTypeSelector = (() => {
    const listeners = new WeakMap();

    return {
        bind: (root, dotnetReference) => {
            if (!root || listeners.has(root)) return;
            const listener = event => {
                const path = event.composedPath?.();
                const inside = path ? path.includes(root) : root.contains(event.target);
                if (!inside) dotnetReference.invokeMethodAsync("OnClickAway").catch(() => {});
            };
            document.addEventListener("pointerup", listener, true);
            document.addEventListener("focusin", listener, true);
            listeners.set(root, listener);
        },
        unbind: root => {
            const listener = root && listeners.get(root);
            if (!listener) return;
            document.removeEventListener("pointerup", listener, true);
            document.removeEventListener("focusin", listener, true);
            listeners.delete(root);
        }
    };
})();

window.kvWiki = (() => {
    const listeners = new WeakMap();
    const observers = new WeakMap();
    let mermaidModule;
    const scrollTo = id => {
        const target = document.getElementById(id);
        if (!target) return false;
        history.replaceState(null, "", `/wiki#${encodeURIComponent(id)}`);
        target.scrollIntoView({ behavior: "smooth", block: "start" });
        return true;
    };
    const scrollToTop = () => window.scrollTo({ top: 0, behavior: "smooth" });
    const copyText = async text => {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            const textarea = document.createElement("textarea");
            textarea.value = text;
            textarea.setAttribute("readonly", "");
            textarea.style.position = "fixed";
            textarea.style.opacity = "0";
            document.body.appendChild(textarea);
            textarea.select();
            const copied = document.execCommand("copy");
            textarea.remove();
            return copied;
        }
    };
    const decorateCodeBlocks = (root, copyLabel, copiedLabel) => {
        root.querySelectorAll("pre").forEach(pre => {
            if (pre.dataset.copyEnabled) return;
            pre.dataset.copyEnabled = "true";
            const wrapper = document.createElement("div");
            wrapper.className = "wiki-code-block";
            const toolbar = document.createElement("div");
            toolbar.className = "wiki-code-toolbar";
            const code = pre.querySelector("code");
            const language = code?.className.match(/language-([\w-]+)/)?.[1] || "text";
            const languageLabel = document.createElement("span");
            languageLabel.className = "wiki-code-language";
            languageLabel.textContent = language;
            const button = document.createElement("button");
            button.type = "button";
            button.className = "wiki-copy-code";
            button.textContent = copyLabel;
            button.addEventListener("click", async () => {
                const success = await copyText(code?.innerText ?? pre.innerText);
                button.textContent = success ? copiedLabel : copyLabel;
                button.classList.toggle("is-copied", success);
                if (success) setTimeout(() => { button.textContent = copyLabel; button.classList.remove("is-copied"); }, 1600);
            });
            toolbar.append(languageLabel, button);
            pre.before(wrapper);
            wrapper.append(toolbar, pre);
        });
    };
    const decorateMermaidBlocks = (diagrams, copyLabel, copiedLabel) => diagrams.forEach(diagram => {
        if (diagram.dataset.copyEnabled) return;
        diagram.dataset.copyEnabled = "true";
        const wrapper = document.createElement("div");
        wrapper.className = "wiki-mermaid-block";
        const toolbar = document.createElement("div");
        toolbar.className = "wiki-mermaid-toolbar";
        const button = document.createElement("button");
        button.type = "button";
        button.className = "wiki-copy-code";
        button.textContent = copyLabel;
        button.addEventListener("click", async () => {
            const success = await copyText(diagram.dataset.source ?? "");
            button.textContent = success ? copiedLabel : copyLabel;
            if (success) setTimeout(() => { button.textContent = copyLabel; }, 1600);
        });
        toolbar.append(button);
        diagram.before(wrapper);
        wrapper.append(diagram, toolbar);
    });
    const renderDiagrams = async (root, errorLabel, copyLabel, copiedLabel) => {
        const diagrams = [...root.querySelectorAll(".mermaid:not([data-rendered])")];
        if (!diagrams.length) return;
        try {
            mermaidModule ??= import("https://cdn.jsdelivr.net/npm/mermaid@11.12.0/dist/mermaid.esm.min.mjs");
            const mermaid = (await mermaidModule).default;
            mermaid.initialize({ startOnLoad: false, securityLevel: "strict", theme: "default" });
            diagrams.forEach(diagram => { diagram.dataset.source = diagram.textContent ?? ""; });
            await mermaid.run({ nodes: diagrams });
            diagrams.forEach(diagram => { diagram.dataset.rendered = "true"; });
            decorateMermaidBlocks(diagrams, copyLabel, copiedLabel);
        } catch {
            diagrams.forEach(diagram => {
                diagram.textContent = `${errorLabel}\n\n${diagram.dataset.source ?? diagram.textContent ?? ""}`;
                diagram.dataset.rendered = "true";
                diagram.classList.add("wiki-mermaid-fallback");
            });
            decorateMermaidBlocks(diagrams, copyLabel, copiedLabel);
        }
    };
    return {
        scrollTo,
        scrollToTop,
        bind: (root, dotnet, copyLabel, copiedLabel, diagramErrorLabel) => {
            if (!root || listeners.has(root)) return;
            const handler = event => {
                const anchor = event.target instanceof Element ? event.target.closest("a[href]") : null;
                if (!(anchor instanceof HTMLAnchorElement)) return;
                const url = new URL(anchor.href, location.href);
                if (url.pathname !== "/wiki" || !url.hash) return;
                event.preventDefault();
                scrollTo(decodeURIComponent(url.hash.slice(1)));
            };
            root.addEventListener("click", handler);
            listeners.set(root, handler);
            const headings = [...root.querySelectorAll("h2[id]")];
            const observer = new IntersectionObserver(entries => {
                const visible = entries.filter(entry => entry.isIntersecting)
                    .sort((left, right) => Math.abs(left.boundingClientRect.top) - Math.abs(right.boundingClientRect.top))[0];
                if (visible) dotnet.invokeMethodAsync("OnSectionChanged", visible.target.id);
            }, { rootMargin: "-12% 0px -72% 0px", threshold: 0 });
            headings.forEach(heading => observer.observe(heading));
            observers.set(root, observer);
            decorateCodeBlocks(root, copyLabel, copiedLabel);
            void renderDiagrams(root, diagramErrorLabel, copyLabel, copiedLabel);
        },
        unbind: root => {
            root?.removeEventListener("click", listeners.get(root));
            listeners.delete(root);
            observers.get(root)?.disconnect();
            observers.delete(root);
        },
        scrollBottom: element => {
            if (element) element.scrollTop = element.scrollHeight;
        }
    };
})();

window.kvGraph = (() => {
    const handlers = new WeakMap();
    let activeScrollElement = null;

    const dispose = scrollElement => {
        const state = handlers.get(scrollElement);
        if (!state) return;
        scrollElement.removeEventListener("pointerdown", state.pointerDown);
        scrollElement.removeEventListener("pointermove", state.pointerMove);
        scrollElement.removeEventListener("pointerup", state.pointerUp);
        scrollElement.removeEventListener("pointercancel", state.pointerUp);
        scrollElement.removeEventListener("click", state.click, true);
        document.removeEventListener("fullscreenchange", state.fullscreenChange);
        handlers.delete(scrollElement);
        if (activeScrollElement === scrollElement) activeScrollElement = null;
    };

    return {
        initialize: (scrollElement, fullscreenElement, dotnetReference) => {
            if (!scrollElement) return;
            if (activeScrollElement && activeScrollElement !== scrollElement) dispose(activeScrollElement);
            if (handlers.has(scrollElement)) return;
            activeScrollElement = scrollElement;
            let drag = null;
            let suppressNextClick = false;
            const pointerDown = event => {
                if (event.pointerType !== "mouse" || event.button !== 0
                    || (event.target instanceof Element && event.target.closest("button, a, input, .mud-popover"))) return;
                drag = { pointerId: event.pointerId, x: event.clientX, y: event.clientY,
                    left: scrollElement.scrollLeft, top: scrollElement.scrollTop, started: false };
            };
            const pointerMove = event => {
                if (!drag || drag.pointerId !== event.pointerId) return;
                if (!drag.started && Math.hypot(event.clientX - drag.x, event.clientY - drag.y) < 5) return;
                if (!drag.started) {
                    drag.started = true;
                    scrollElement.classList.add("panning");
                    scrollElement.setPointerCapture(event.pointerId);
                }
                scrollElement.scrollLeft = drag.left - (event.clientX - drag.x);
                scrollElement.scrollTop = drag.top - (event.clientY - drag.y);
            };
            const pointerUp = event => {
                if (!drag || drag.pointerId !== event.pointerId) return;
                if (drag.started && scrollElement.hasPointerCapture(event.pointerId)) {
                    scrollElement.releasePointerCapture(event.pointerId);
                    suppressNextClick = true;
                }
                drag = null;
                scrollElement.classList.remove("panning");
            };
            const click = event => {
                if (!suppressNextClick) return;
                suppressNextClick = false;
                event.preventDefault();
                event.stopPropagation();
            };
            const fullscreenChange = () => dotnetReference.invokeMethodAsync(
                "OnFullscreenChanged", document.fullscreenElement === fullscreenElement);

            scrollElement.addEventListener("pointerdown", pointerDown);
            scrollElement.addEventListener("pointermove", pointerMove);
            scrollElement.addEventListener("pointerup", pointerUp);
            scrollElement.addEventListener("pointercancel", pointerUp);
            scrollElement.addEventListener("click", click, true);
            document.addEventListener("fullscreenchange", fullscreenChange);
            handlers.set(scrollElement, { pointerDown, pointerMove, pointerUp, click, fullscreenChange, fullscreenElement });
        },
        toggleFullscreen: async element => {
            if (!document.fullscreenEnabled) return false;
            if (document.fullscreenElement === element) await document.exitFullscreen();
            else await element.requestFullscreen();
            return document.fullscreenElement === element;
        },
        dispose: scrollElement => {
            dispose(scrollElement);
        }
    };
})();

window.kvLogs = { scrollBottom: element => { if (element) element.scrollTop = element.scrollHeight; } };
