/**
 * Sets the extension context type on globalThis.__EXT_CONTEXT__.
 * This module is loaded by HTML pages before Blazor starts to indicate
 * which context (TAB, POPUP, SIDEPANEL, OPTIONS) the app is running in.
 *
 * Context is auto-detected from the HTML filename:
 *   indexInPopup.html     → POPUP
 *   indexInSidePanel.html → SIDEPANEL
 *   indexInTab.html       → TAB
 *   index.html (or other) → TAB (default)
 *
 * The ?ctx= query parameter can still override auto-detection if needed:
 *   <script type="module" src="scripts/es6/setExtContext.js?ctx=POPUP"></script>
 */

// Auto-detect context from the page's own filename
function detectContextFromFilename(): string {
    const path = globalThis.location?.pathname ?? '';
    if (path.includes('indexInPopup')) return 'POPUP';
    if (path.includes('indexInSidePanel')) return 'SIDEPANEL';
    return 'TAB';
}

// Allow explicit ?ctx= override, otherwise auto-detect from filename
const scriptUrl = import.meta.url;
const url = new URL(scriptUrl);
const explicitCtx = url.searchParams.get('ctx');
const contextType = explicitCtx ?? detectContextFromFilename();

// Set the context on globalThis
(globalThis as unknown as { __EXT_CONTEXT__: { type: string } }).__EXT_CONTEXT__ = {
    type: contextType
};

console.debug(`setExtContext: Set globalThis.__EXT_CONTEXT__.type = "${contextType}"${explicitCtx ? ' (explicit)' : ' (auto-detected)'}`);
