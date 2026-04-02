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

console.debug(`[${contextType}] setExtContext: globalThis.__EXT_CONTEXT__.type = "${contextType}"`);
