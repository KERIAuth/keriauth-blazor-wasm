/**
 * Sets the extension context type on globalThis.__EXT_CONTEXT__.
 * This module is loaded by index.html before Blazor starts to indicate
 * which context (TAB, POPUP, SIDEPANEL, BW) the app is running in.
 *
 * Context is detected from the page's ?context= query parameter:
 *   index.html?context=popup     → POPUP
 *   index.html?context=sidepanel → SIDEPANEL
 *   index.html                   → TAB (default)
 *
 * Service worker (BackgroundWorker) context is detected automatically → BW
 *
 * When the query parameter is missing (e.g., side panel or popup opened before the
 * service worker's setOptions/setPopup call completes), the synchronous detection
 * defaults to TAB. The __verifyExtContext function uses chrome.runtime.getContexts()
 * to asynchronously determine the true context and is called by App.razor.
 */

// Detect context from service worker environment or page URL query parameter
function detectContext(): string {
    if (typeof (globalThis as any).ServiceWorkerGlobalScope !== 'undefined') return 'BW';
    const params = new URLSearchParams(globalThis.location?.search ?? '');
    const ctx = params.get('context')?.toUpperCase();
    if (ctx === 'POPUP') return 'POPUP';
    if (ctx === 'SIDEPANEL') return 'SIDEPANEL';
    return 'TAB';
}

const contextType = detectContext();

// Set the context on globalThis
(globalThis as unknown as { __EXT_CONTEXT__: { type: string } }).__EXT_CONTEXT__ = {
    type: contextType
};

console.debug(`setExtContext.ts: [${contextType}] globalThis.__EXT_CONTEXT__.type`);

/**
 * Async verification of extension context using chrome.tabs.getCurrent() and
 * chrome.runtime.getContexts(). Called by App.razor when the synchronous
 * detection defaulted to TAB.
 *
 * chrome.tabs.getCurrent() returns a Tab for actual browser tabs but undefined
 * for non-tab contexts (popups, side panels). When undefined, getContexts()
 * determines whether we're a SIDE_PANEL or POPUP.
 *
 * Note: Chrome reports side panels as both TAB and SIDE_PANEL in getContexts(),
 * so SIDE_PANEL must be checked before TAB.
 */
(globalThis as any).__verifyExtContext = async (): Promise<string> => {
    try {
        if (typeof chrome === 'undefined') return contextType;

        // chrome.tabs.getCurrent() returns a Tab only for pages in actual browser tabs.
        // Popups and side panels get undefined.
        if (chrome.tabs?.getCurrent) {
            const tab = await chrome.tabs.getCurrent();
            if (tab) return 'TAB';
        }

        // Not in a tab — determine if SIDE_PANEL or POPUP
        if (chrome.runtime?.getContexts) {
            const contexts = await chrome.runtime.getContexts({
                contextTypes: ['SIDE_PANEL' as chrome.runtime.ContextType, 'POPUP' as chrome.runtime.ContextType]
            });
            // Check SIDE_PANEL before POPUP (side panels are also reported as tabs by Chrome)
            for (const ctx of contexts) {
                if (ctx.contextType === 'SIDE_PANEL') return 'SIDEPANEL';
            }
            for (const ctx of contexts) {
                if (ctx.contextType === 'POPUP') return 'POPUP';
            }
        }
    } catch {
        // API not available or failed — fall through to sync result
    }
    return contextType;
};

/**
 * Updates __EXT_CONTEXT__.type after async verification.
 * Called by App.razor after __verifyExtContext resolves to a different context.
 */
(globalThis as any).__updateExtContext = (verified: string): void => {
    (globalThis as unknown as { __EXT_CONTEXT__: { type: string } }).__EXT_CONTEXT__.type = verified;
    console.debug(`setExtContext.ts: [${verified}] context updated after verification`);
};
