/**
 * Gate for console.debug across all extension contexts (BW, App pages, content scripts).
 *
 * Default: console.debug is replaced with a no-op so per-call cost (chrome console buffer
 * write, DevTools serialization) is eliminated. The user can flip it on via the Preferences
 * page; chrome.storage.onChanged makes the change take effect live across all contexts
 * within a single event tick — no service worker or page reload required.
 *
 * Trade-off: argument evaluation at the call site (template literal interpolation, function
 * arguments) still runs because the JS engine evaluates them before the function call. The
 * win is eliminating the no-op'd function body and any DevTools UI cost. To skip argument
 * evaluation entirely a build-time strip would be required.
 *
 * Caveat: this only gates callers that resolve console.debug at call time (the normal
 * `console.debug(...)` pattern). Any code that captures the function reference at module
 * load (`const debug = console.debug; debug(...)`) bypasses the patch. All current call
 * sites use the direct form; this is a guideline for future code.
 *
 * IMPORTANT: This module must be the first import in each context entry point (app.ts,
 * ContentScript.ts) so the patch is in place before any other module's logging runs.
 */

const STORAGE_KEY = 'Preferences';
const PREF_FIELD = 'IsConsoleDebugLogged';

const _origConsoleDebug = console.debug.bind(console);
const _noopDebug = () => { /* gated */ };

const setEnabled = (enabled: boolean): void => {
    console.debug = enabled ? _origConsoleDebug : _noopDebug;
};

// Default off until we read the actual preference.
setEnabled(false);

// chrome.storage may be unavailable in some non-extension test environments. Fail silent.
if (typeof chrome !== 'undefined' && chrome.storage?.local) {
    chrome.storage.local.get(STORAGE_KEY).then(result => {
        const enabled = !!result?.[STORAGE_KEY]?.[PREF_FIELD];
        setEnabled(enabled);
    }).catch(() => {
        // First run / no Preferences yet — stay off.
    });

    chrome.storage.onChanged.addListener((changes, area) => {
        if (area !== 'local' || !changes[STORAGE_KEY]) {
            return;
        }
        const enabled = !!changes[STORAGE_KEY].newValue?.[PREF_FIELD];
        setEnabled(enabled);
    });
}
