/**
 * Sets the extension context type on globalThis.__EXT_CONTEXT__.
 * This module is loaded by HTML pages before Blazor starts to indicate
 * which context (TAB, POPUP, SIDEPANEL, OPTIONS) the app is running in.
 *
 * Usage: Import with a query parameter to set the context type:
 *   <script type="module" src="scripts/es6/setExtContext.js?ctx=TAB"></script>
 *   <script type="module" src="scripts/es6/setExtContext.js?ctx=POPUP"></script>
 *   <script type="module" src="scripts/es6/setExtContext.js?ctx=SIDEPANEL"></script>
 *   <script type="module" src="scripts/es6/setExtContext.js?ctx=OPTIONS"></script>
 */

// Get the context type from the script's URL query parameter
const scriptUrl = import.meta.url;
const url = new URL(scriptUrl);
const contextType = url.searchParams.get('ctx') ?? 'UNKNOWN';

// Set the context on globalThis
(globalThis as unknown as { __EXT_CONTEXT__: { type: string } }).__EXT_CONTEXT__ = {
    type: contextType
};

console.log(`setExtContext: Set globalThis.__EXT_CONTEXT__.type = "${contextType}"`);
