/// <reference types="chrome-types" />
/*
 * For details, see https://mingyaulee.github.io/Blazor.BrowserExtension/app-js
 */

// CRITICAL: Import libsodium polyfill FIRST, before any other module imports
// This ensures all crypto polyfills are in place before libsodium WASM initializes
// See commits 90aa450 and 6f33fba for details
import './scripts/es6/libsodium-polyfill.js';

// Determine logging context tag for this runtime
const _logTag: string = (() => {
    if (typeof (globalThis as any).ServiceWorkerGlobalScope !== 'undefined') return '[BW]';
    const ctx = (globalThis as any).__EXT_CONTEXT__?.type;
    switch (ctx) {
        case 'POPUP': return '[AppPopup]';
        case 'TAB': return '[AppTab]';
        case 'SIDEPANEL': return '[AppSidePanel]';
        default: return '[App]';
    }
})();

// Polyfills for libsodium WASM initialization
//
// 1. Polyfill global object (service workers only have 'self', not 'global' or 'window')
// This must be set BEFORE any module imports libsodium
if (typeof self !== 'undefined' && typeof (globalThis as any).global === 'undefined') {
    (globalThis as any).global = self;
    console.debug(`app.ts: ${_logTag} Polyfilled global object for libsodium`);
}

// Note: crypto.randomBytes, window.crypto fix, and Module.getRandomValue
// are now handled by libsodium-polyfill.js imported above

import { PRODUCT_NAME } from './scripts/es6/brand.js';

// Static imports - work in both ServiceWorker (backgroundWorker) and standard contexts
// Import modules with names so they're registered in the module system
// NOTE: signifyClient is NOT statically imported here because:
// - signifyClient: contains libsodium which needs crypto APIs ready first
// NOTE: WebAuthn modules (navigatorCredentialsShim, aesGcmCrypto) are loaded via JsModuleLoader
// and cached by browser module system - no static import needed here
// import * as signifyClient from './scripts/esbuild/signifyClient.js';
// import * as utils from './scripts/esbuild/utils.js';

// Make modules available globally for debugging
(globalThis as any).appModules = {};  // WebAuthn modules loaded dynamically by JsModuleLoader










// TODO P3 rename to use PRODUCT_NAME-based prefix
const CS_ID_PREFIX = 'keriauth-cs';

// True when running inside the BackgroundWorker service worker context.
// Used to gate module-level chrome.* listener registrations so they run
// during initial script evaluation (required to catch cold-start events).
const _isSW = typeof (globalThis as any).ServiceWorkerGlobalScope !== 'undefined';

// WASM readiness gate (SW context only).
// Resolved by afterStarted() once the Blazor runtime is fully loaded.
// CLIENT_SW_WAKE handlers hold their response until this resolves,
// so clients get a single definitive answer without polling.
let _wasmReadyResolve: () => void;
const _wasmReady = new Promise<void>(resolve => { _wasmReadyResolve = resolve; });

// Type definitions for Blazor Browser Extension types
interface WebAssemblyStartOptions {
    [key: string]: unknown;
}

interface BrowserExtensionInstance {
    BrowserExtension: {
        Mode: string;
    };
}

interface PingMessage {
    type: 'ping';
}

interface PingResponse {
    ok: boolean;
}

// ==================================================================================
// HOISTED HELPERS FOR BACKGROUND WORKER
// ==================================================================================
// These functions are defined at module level so they can be used by chrome.* event
// listeners registered at module level (required to catch cold-start wake events).
// They are harmless no-ops in App (Standard/Debug) contexts since nothing calls them there.

const matchPatternsFromTabUrl = (tabUrl: string): string[] => {
    try {
        const u = new URL(tabUrl);
        if (u.protocol === "http:" || u.protocol === "https:") {
            return [`${u.protocol}//${u.host}/*`];
        }
    } catch (e) {
        // file:///*, about:blank, data:, chrome://, chrome-extension://, etc.
    }
    return [];
};

const hostWithPortFromPattern = (matchPattern: string): string => {
    try {
        const urlString = matchPattern.slice(0, matchPattern.length - 2);
        const u = new URL(urlString);
        return u.host;
    } catch (e) {
        console.error(`app.ts: ${_logTag} Invalid match pattern: ${matchPattern}`);
        return '';
    }
};

const scriptIdFromHostWithPort = (hostWithPort: string): string => {
    return `${CS_ID_PREFIX}-${hostWithPort}`;
};

const isSupportedUrlScheme = (url: string | undefined): boolean => {
    if (!url) return false;
    try {
        const u = new URL(url);
        return u.protocol === 'http:' || u.protocol === 'https:';
    } catch {
        return false;
    }
};

const hasRegisteredContentScript = async (url: string): Promise<boolean> => {
    const patterns = matchPatternsFromTabUrl(url);
    if (patterns.length === 0) return false;

    const hostWithPort = hostWithPortFromPattern(patterns[0]);
    if (!hostWithPort) return false;

    const scriptId = scriptIdFromHostWithPort(hostWithPort);
    try {
        const registered = await chrome.scripting.getRegisteredContentScripts({ ids: [scriptId] });
        return registered.length > 0;
    } catch {
        return false;
    }
};

const promptAndReloadTab = async (tabId: number, message: string, context: string): Promise<boolean> => {
    try {
        const reloadResponse = await chrome.scripting.executeScript({
            target: { tabId },
            func: (msg: string) => confirm(msg),
            args: [message]
        } as any);

        const userAccepted = reloadResponse?.[0]?.result === true;

        if (userAccepted) {
            console.log(`app.ts: ${_logTag} User accepted reload prompt (${context}) - reloading tab ${tabId}`);
            await chrome.tabs.reload(tabId);
            return true;
        } else {
            console.debug(`app.ts: ${_logTag} User declined reload prompt (${context}) for tab ${tabId}`);
            return false;
        }
    } catch (error) {
        console.error(`app.ts: ${_logTag} ERROR in promptAndReloadTab (${context}) for tab ${tabId}:`, error);
        throw error;
    }
};

const tabIconState = new Map<number, boolean>();

// chrome.action.setIcon with path fails in service workers on macOS Chrome ("Failed to fetch").
// Workaround: fetch images ourselves, render to OffscreenCanvas, and pass ImageData directly.
const loadIconImageData = async (path: string, size: number): Promise<ImageData> => {
    const url = chrome.runtime.getURL(path);
    const response = await fetch(url);
    const blob = await response.blob();
    const bitmap = await createImageBitmap(blob);
    const canvas = new OffscreenCanvas(size, size);
    const ctx = canvas.getContext("2d")!;
    ctx.drawImage(bitmap, 0, 0, size, size);
    bitmap.close();
    return ctx.getImageData(0, 0, size, size);
};

const updateIconForTab = async (tabId: number, isActive: boolean): Promise<void> => {
    if (tabIconState.get(tabId) === isActive) return;

    const iconPrefix = isActive ? "logo" : "logob";
    const sizes = [16, 24, 32, 48, 128] as const;

    try {
        const imageDataEntries = await Promise.all(
            sizes.map(async (size) => [size, await loadIconImageData(`images/${iconPrefix}${size.toString().padStart(3, '0')}.png`, size)] as const)
        );
        const imageData: Record<string, ImageData> = {};
        for (const [size, data] of imageDataEntries) {
            imageData[size.toString()] = data;
        }
        await chrome.action.setIcon({ imageData, tabId });
        tabIconState.set(tabId, isActive);
        console.debug(`app.ts: ${_logTag} Set ${isActive ? 'active' : 'inactive'} icon for tab ${tabId}`);
    } catch (error) {
        tabIconState.delete(tabId);
        console.warn(`app.ts: ${_logTag} Failed to set ${isActive ? 'active' : 'inactive'} icon for tab ${tabId}:`, error);
    }
};

const openPopupIfNotOpen = async (): Promise<void> => {
    // console.debug("app.ts: openPopupIfNotOpen - entering");
    try {
        // Check if popup or sidepanel is already open
        // console.debug("app.ts: openPopupIfNotOpen - checking for existing contexts");
        const contexts = await chrome.runtime.getContexts({
            contextTypes: ['POPUP', 'SIDE_PANEL']
        });
        // console.debug("app.ts: openPopupIfNotOpen - found contexts:", contexts.length, contexts.map(c => c.contextType));

        if (contexts.length > 0) {
            console.debug(`app.ts: ${_logTag} Popup or sidepanel already open, skipping openPopup`);
            return;
        }

        // Set the popup URL (required before openPopup when no default_popup in manifest)
        // console.debug("app.ts: openPopupIfNotOpen - setting popup URL");
        await chrome.action.setPopup({ popup: '/indexInPopup.html' });

        // Open the popup
        // console.debug("app.ts: openPopupIfNotOpen - calling openPopup()");
        try {
            await chrome.action.openPopup();
            console.log(`app.ts: ${_logTag} Popup opened successfully`);
        } catch (error) {
            // openPopup() sometimes throws even on success
            console.debug(`app.ts: ${_logTag} openPopup() threw (may still have succeeded):`, error);
        }

        // Clear the popup setting so future clicks trigger onClicked event
        // console.debug("app.ts: openPopupIfNotOpen - clearing popup URL");
        await chrome.action.setPopup({ popup: '' });
        // console.debug("app.ts: openPopupIfNotOpen - completed");

    } catch (error) {
        console.error(`app.ts: ${_logTag} Error in openPopupIfNotOpen:`, error);
    }
};

// ==================================================================================
// MODULE-LEVEL LISTENER REGISTRATIONS (SERVICE WORKER ONLY)
// ==================================================================================
// These listeners MUST be registered during initial script evaluation (module load)
// so they catch chrome events that wake the service worker from an inactive state.
// If registered later (e.g., inside beforeStart()), the wake event is already dispatched
// and the listener misses it. See BackgroundWorkerRunner.js fromReference() for context.
if (_isSW) {
    // Clean up icon cache when tabs are closed
    chrome.tabs.onRemoved.addListener((tabId) => {
        tabIconState.delete(tabId);
    });

    // ==================================================================================
    // ACTION BUTTON CLICK HANDLER
    // ==================================================================================
    //
    // REQUIREMENT SCENARIOS (Action Button Click):
    //
    // Scenario #1: Fresh site (No Permission, No Script, No Active CS)
    //   User Action: Click action button
    //   Expected: 1) Show permission prompt
    //             2) If granted: Inject one-shot script
    //             3) Register persistent script
    //             4) Prompt user to reload page
    //
    // Scenario #2: Pre-granted permission (Permission Exists, No Script, No Active CS)
    //   User Action: Click action button
    //   Expected: 1) No permission prompt (request returns true immediately)
    //             2) Inject one-shot script
    //             3) Register persistent script
    //             4) Prompt user to reload page
    //
    // Scenario #3: Re-grant after removal (Permission Exists, No Script, Active CS in page)
    //   User Action: Click action button
    //   Expected: 1) No permission prompt
    //             2) Skip one-shot injection (CS already in page)
    //             3) Register persistent script
    //             4) Prompt user to reload (clean state)
    //
    // Scenario #4: Already fully active (Permission Exists, Script Registered, Active CS)
    //   User Action: Click action button
    //   Expected: 1) Ping content script successfully
    //             2) No action needed - return early (silent success)
    //
    // Scenario #5: Stale registration (Permission Exists, Script Registered, No Active CS)
    //   User Action: Click action button
    //   Expected: 1) Ping content script - no response
    //             2) Prompt user to reload (reconnect)
    //
    // Scenario #6: User declines permission (No Permission)
    //   User Action: Click action button, then decline permission prompt
    //   Expected: 1) Show permission prompt
    //             2) User declines
    //             3) Stop - no further action
    //
    // ==================================================================================

    chrome.action.onClicked.addListener(async (tab: chrome.tabs.Tab) => {
        console.debug(`app.ts: ${_logTag} ACTION CLICKED - tab:`, tab?.id, "url:", tab?.url?.substring(0, 80));
        try {
            if (!tab?.id || !tab.url) {
                console.warn(`app.ts: ${_logTag} ACTION CLICKED - missing tab.id or tab.url, returning early`);
                return;
            }

            // 1) Compute per-origin match patterns from the clicked tab
            const MATCHES = matchPatternsFromTabUrl(tab.url);
            console.debug(`app.ts: ${_logTag} ACTION CLICKED - computed MATCHES:`, MATCHES);
            if (MATCHES.length === 0) {
                // Tab URL doesn't support content scripts (chrome-extension:, about:blank, etc.)
                // Still open the popup so user can interact with the extension
                console.debug(`app.ts: ${_logTag} Unsupported or restricted URL scheme; opening popup without content script setup.`, tab.url);
                await openPopupIfNotOpen();
                return;
            }

            // 2) Request persistent host permission
            // CRITICAL: request() must be the VERY FIRST async operation to preserve user gesture
            // Note: chrome.permissions.request() returns true immediately if permission already exists
            // (no prompt shown to user in that case)
            // Implements: Scenario #1, #2, #6 (permission handling)
            const wanted = { origins: MATCHES };
            // console.debug("app.ts: ACTION CLICKED - requesting permissions:", wanted);
            try {
                const granted = await chrome.permissions.request(wanted);
                console.debug(`app.ts: ${_logTag} Permission request completed, granted:`, granted);
                if (!granted) {
                    // Scenario #6: User declined permission
                    console.debug(`app.ts: ${_logTag} User declined persistent host permission; stopping.`);
                    return;
                }
                console.debug(`app.ts: ${_logTag} Permission granted (or was already granted)`);
            } catch (error) {
                console.error(`app.ts: ${_logTag} ERROR in chrome.permissions.request():`, error);
                throw error;
            }

            // 3) Check if persistent content script is already registered for this origin
            // Implements: Branch point for Scenarios #4, #5 vs #1, #2, #3
            const hostWithPort = hostWithPortFromPattern(MATCHES[0] || '');
            const scriptId = scriptIdFromHostWithPort(hostWithPort);
            // console.debug("app.ts: ACTION CLICKED - checking registration for scriptId:", scriptId);
            let already: chrome.scripting.RegisteredContentScript[] = [];
            try {
                already = await chrome.scripting.getRegisteredContentScripts({ ids: [scriptId] });
                // console.debug("app.ts: Script registration check completed, found:", already.length, already);
            } catch (error) {
                console.error(`app.ts: ${_logTag} ERROR in chrome.scripting.getRegisteredContentScripts():`, error);
                throw error;
            }
            if (already.length > 0) {
                // Script is already registered - check if it's active or stale
                // Implements: Scenario #4 (active) or Scenario #5 (stale)
                // console.debug("app.ts: Persistent content script already registered, pinging tab", tab.id);

                // Check if there's a stale content script from a previous extension installation
                // This can happen when extension is uninstalled/reinstalled and page wasn't reloaded
                try {
                    const pingResponse = await chrome.tabs.sendMessage(tab.id, { type: 'ping' });
                    // console.debug("app.ts: ACTION CLICKED - ping response:", pingResponse);
                    if (pingResponse?.ok) {
                        // Scenario #4: Content script is active and responding
                        // console.debug("app.ts: Content script is active and responding");
                        await updateIconForTab(tab.id, true);
                        // Open popup for user interaction (if not already open)
                        // console.debug("app.ts: ACTION CLICKED - calling openPopupIfNotOpen");
                        await openPopupIfNotOpen();
                        console.debug(`app.ts: ${_logTag} ACTION CLICKED - openPopupIfNotOpen completed`);
                        return;
                    } else {
                        // Ping response received but ok is not true - treat as stale
                        // console.warn("app.ts: ACTION CLICKED - ping response received but ok is not true:", pingResponse);
                        await updateIconForTab(tab.id, false);
                        await promptAndReloadTab(
                            tab.id,
                            `${PRODUCT_NAME} needs to refresh the connection with this page.\n\nReload to ensure proper functionality?`,
                            'ping response not ok'
                        );
                        return;
                    }
                } catch (error) {
                    // Scenario #5: Content script registered but not responding (stale)
                    // console.warn("app.ts: ACTION CLICKED - Content script registered but ping threw error:", error);
                    await updateIconForTab(tab.id, false);
                    await promptAndReloadTab(
                        tab.id,
                        `${PRODUCT_NAME} needs to refresh the connection with this page.\n\nReload to ensure proper functionality?`,
                        'stale script'
                    );
                    return;
                }
            } else {
                // Script NOT registered yet - Implements: Scenario #1, #2, or #3
                // console.debug("app.ts: ACTION CLICKED - No persistent content script registered yet - proceeding", { MATCHES, scriptId });

                // 4) Check if content script is already active in the page
                // This can happen if permission was removed and then re-added on same tab
                // The content script stays in the page even after permission removal
                // Branch point: Scenario #3 (CS active) vs Scenario #1/#2 (CS not active)
                let contentScriptAlreadyActive = false;
                // console.debug("app.ts: ACTION CLICKED - pinging to check if CS already active in page");
                try {
                    const pingResponse = await chrome.tabs.sendMessage(tab.id, { type: 'ping' });
                    // console.debug("app.ts: ACTION CLICKED - ping response (script NOT registered branch):", pingResponse);
                    if (pingResponse?.ok) {
                        contentScriptAlreadyActive = true;
                        console.debug(`app.ts: ${_logTag} Content script already active (likely permission re-grant)`);
                    }
                } catch (error) {
                    console.debug(`app.ts: ${_logTag} ACTION CLICKED - No active content script detected, error:`, error);
                }

                // Scenario #3: Content script already in page (permission re-grant case)
                if (contentScriptAlreadyActive) {
                    // console.debug("app.ts: Skipping one-shot injection, registering persistent script and prompting reload");

                    // Register persistent content script
                    try {
                        await chrome.scripting.registerContentScripts([{
                            id: scriptId,
                            js: ["scripts/esbuild/ContentScript.js"],
                            matches: MATCHES,
                            runAt: "document_idle",
                            allFrames: true,
                            world: "ISOLATED",
                            persistAcrossSessions: true,
                        }]);
                        // console.debug("app.ts: Registered persistent content script:", scriptId);
                    } catch (error) {
                        console.error(`app.ts: ${_logTag} ERROR in chrome.scripting.registerContentScripts() [contentScriptAlreadyActive branch]:`, error);
                        throw error;
                    }

                    await updateIconForTab(tab.id, true);

                    // Strongly recommend reload to clear any stale state
                    await promptAndReloadTab(
                        tab.id,
                        `${PRODUCT_NAME} is now enabled for this site.\n\nReload the page to ensure clean state?`,
                        'contentScriptAlreadyActive'
                    );
                    return;
                }

                // Scenario #1 & #2: Fresh site or pre-granted permission - need full setup
                // 5) Inject a one-shot script into the current page
                let oneShotResult;
                try {
                    oneShotResult = await chrome.scripting.executeScript({
                        target: { tabId: tab.id },
                        files: ["scripts/esbuild/ContentScript.js"],
                        injectImmediately: false,
                        world: "ISOLATED",
                    });
                    console.log(`app.ts: ${_logTag} One-shot injection completed successfully`);
                } catch (error) {
                    console.error(`app.ts: ${_logTag} ERROR in chrome.scripting.executeScript() [one-shot injection]:`, error);
                    throw error;
                }

                if (!oneShotResult?.[0]?.documentId) {
                    console.error(`app.ts: ${_logTag} One-shot injection failed.`, oneShotResult);
                    return;
                }

                await updateIconForTab(tab.id, true);

                // 6) Register a persistent content script for this origin
                // Note: May encounter race condition with onAdded listener (see comment below)
                try {
                    await chrome.scripting.registerContentScripts([{
                        id: scriptId,
                        js: ["scripts/esbuild/ContentScript.js"],
                        matches: MATCHES,          // derived from the tab's origin
                        runAt: "document_idle",    // or "document_start"/"document_end"
                        allFrames: true,
                        world: "ISOLATED",
                        persistAcrossSessions: true, // default is true
                    }]);
                    console.log(`app.ts: ${_logTag} Registered persistent content script:`, scriptId, "for", MATCHES);
                } catch (error: any) {
                    // Handle race condition: onAdded listener may have already registered the script
                    // when permission was just granted via the permission prompt in Scenario #1
                    // This is expected behavior - we continue to the reload prompt regardless
                    if (error?.message?.includes('Duplicate script ID')) {
                        console.debug(`app.ts: ${_logTag} Script already registered (likely by onAdded listener) - continuing to reload prompt`);
                    } else {
                        console.error(`app.ts: ${_logTag} ERROR in chrome.scripting.registerContentScripts() [main flow]:`, error);
                        throw error;
                    }
                }

                // 7) Prompt user to reload page to activate persistent content script
                // Implements: Final step of Scenario #1 & #2 - user must reload to activate persistent script
                // console.debug("app.ts: ACTION CLICKED - prompting for reload (main flow)");
                await promptAndReloadTab(
                    tab.id,
                    `${PRODUCT_NAME} is now enabled for this site.\n\nReload the page to activate?`,
                    'main flow'
                );
                // console.debug("app.ts: ACTION CLICKED - main flow completed");
            }
            // console.debug("app.ts: ACTION CLICKED - handler completed normally for tab", tab.id);
        } catch (error: any) {
            console.error(`app.ts: ${_logTag} ACTION CLICKED - Error in action.onClicked handler:`, error);
        }
    });

    // ==================================================================================
    // CONTEXT MENU: OPEN SIDE PANEL
    // ==================================================================================
    // Must call chrome.sidePanel.open() immediately (no async work before it)
    // to preserve the user gesture. See https://groups.google.com/a/chromium.org/g/chromium-extensions/c/d5ky9SiZlqQ
    chrome.contextMenus.onClicked.addListener((info: chrome.contextMenus.OnClickData, tab?: chrome.tabs.Tab) => {
        if (info.menuItemId !== 'openSidePanel') return;
        if (!tab?.id) return;
        chrome.sidePanel.open({ tabId: tab.id }).catch((err: unknown) => {
            console.warn(`app.ts: ${_logTag} sidePanel.open failed:`, err);
        });
    });

    // ==================================================================================
    // HELPER FUNCTIONS FOR MODULE-LEVEL LISTENERS
    // ==================================================================================

    /**
     * Unregisters content script for a given match pattern.
     * @param matchPattern Pattern like "http://foo.example.com:8080/*"
     */
    const unregisterForPattern = async (matchPattern: string): Promise<void> => {
        const hostWithPort = hostWithPortFromPattern(matchPattern);
        if (!hostWithPort) {
            console.error(`app.ts: ${_logTag} Cannot unregister - invalid pattern: ${matchPattern}`);
            return;
        }
        const scriptId = scriptIdFromHostWithPort(hostWithPort);
        try {
            await chrome.scripting.unregisterContentScripts({ ids: [scriptId] });
            console.debug(`app.ts: ${_logTag} Unregistered content script: ${scriptId}`);
        } catch (e) {
            // Script may not exist, ignore error
            console.debug(`app.ts: ${_logTag} Script ${scriptId} not found for unregistration (may not exist)`);
        }
    };

    /**
     * Initialize icon state for a specific tab.
     * Checks for registered content script and pings to determine active state.
     * @param tabId The tab ID to initialize
     * @param tabUrl The tab URL
     */
    const initializeTabIconState = async (tabId: number, tabUrl: string): Promise<void> => {
        // Skip unsupported URL schemes (chrome://, about:, etc.)
        if (!isSupportedUrlScheme(tabUrl)) {
            await updateIconForTab(tabId, false);
            return;
        }

        // Check if we have a registered content script for this origin
        const hasScript = await hasRegisteredContentScript(tabUrl);
        if (!hasScript) {
            await updateIconForTab(tabId, false);
            return;
        }

        // Try to ping the content script
        try {
            const pingResponse = await chrome.tabs.sendMessage(tabId, { type: 'ping' });
            const isActive = pingResponse?.ok === true;
            await updateIconForTab(tabId, isActive);
        } catch (error) {
            // Content script registered but not responding
            // This is expected after extension reload - CS context is invalidated
            console.debug(`app.ts: ${_logTag} initializeTabIconState - Content script registered but not responding for tab ${tabId} (may need page reload), error:`, error);
            await updateIconForTab(tabId, false);
        }
    };

    /**
     * Initialize icon state for the current active tab on service worker startup.
     * Queries registered content scripts and pings them to determine active state.
     */
    const initializeActiveTabState = async (): Promise<void> => {
        try {
            // Get the currently active tab in the focused window
            // Note: lastFocusedWindow handles cases where currentWindow may not be set
            // (e.g., during browser startup before a window is focused)
            const [activeTab] = await chrome.tabs.query({ active: true, lastFocusedWindow: true });

            if (!activeTab?.id || !activeTab.url) {
                return;
            }

            await initializeTabIconState(activeTab.id, activeTab.url);
        } catch (error) {
            console.error(`app.ts: ${_logTag} initializeActiveTabState - Error during active tab initialization:`, error);
        }
    };

    // ==================================================================================
    // TAB EVENT LISTENERS FOR ICON UPDATES
    // ==================================================================================

    chrome.tabs.onActivated.addListener(async (activeInfo) => {
        // Get tab info to check URL before attempting to ping
        let tab: chrome.tabs.Tab | undefined;
        try {
            tab = await chrome.tabs.get(activeInfo.tabId);
        } catch {
            // Tab may have been closed
            return;
        }

        // Skip unsupported URL schemes (chrome://, about:, etc.) - no content script possible
        if (!isSupportedUrlScheme(tab.url)) {
            await updateIconForTab(activeInfo.tabId, false);
            return;
        }

        // Only ping if we have a registered content script for this origin
        const hasScript = await hasRegisteredContentScript(tab.url!);
        if (!hasScript) {
            await updateIconForTab(activeInfo.tabId, false);
            return;
        }

        try {
            const pingResponse = await chrome.tabs.sendMessage(activeInfo.tabId, { type: 'ping' });
            await updateIconForTab(activeInfo.tabId, pingResponse?.ok === true);
        } catch {
            // Content script registered but not responding (page may need reload)
            await updateIconForTab(activeInfo.tabId, false);
        }
    });

    chrome.tabs.onUpdated.addListener(async (tabId, changeInfo, tab) => {
        // Chrome resets per-tab icons on navigation, so invalidate our cache
        if (changeInfo.status === 'loading') {
            tabIconState.delete(tabId);
            return;
        }

        // Only check when page finishes loading
        if (changeInfo.status !== 'complete') return;

        // Skip unsupported URL schemes (chrome://, about:, etc.) - no content script possible
        if (!isSupportedUrlScheme(tab.url)) {
            await updateIconForTab(tabId, false);
            return;
        }

        // Only ping if we have a registered content script for this origin
        const hasScript = await hasRegisteredContentScript(tab.url!);
        if (!hasScript) {
            await updateIconForTab(tabId, false);
            return;
        }

        try {
            const pingResponse = await chrome.tabs.sendMessage(tabId, { type: 'ping' });
            await updateIconForTab(tabId, pingResponse?.ok === true);
        } catch {
            // Content script registered but not responding (page may need reload)
            await updateIconForTab(tabId, false);
        }
    });

    // ==================================================================================
    // CONTEXT MENU PERMISSION CHANGE HANDLERS
    // ==================================================================================
    //
    // REQUIREMENT SCENARIOS (Permission Added via Context Menu):
    //
    // Scenario #7: Grant permission via context menu (No Script, No Active CS)
    //   User Action: Right-click extension -> "When you click the extension" or "On this site"
    //   Expected: 1) onAdded listener fires
    //             2) Register persistent script
    //             3) Find all matching tabs
    //             4) Prompt each tab to reload
    //
    // Scenario #8: Grant permission via context menu (Script Already Registered, No Active CS)
    //   User Action: Right-click extension -> Grant permission
    //   Expected: 1) onAdded fires
    //             2) Detect script already registered - skip registration
    //             3) Find matching tabs without active CS
    //             4) Prompt those tabs to reload
    //
    // Scenario #9: Grant permission via context menu (Script Registered, Active CS Exists)
    //   User Action: Right-click extension -> Grant permission
    //   Expected: 1) onAdded fires
    //             2) Detect script already registered - skip
    //             3) Ping tabs - content script responds
    //             4) No reload prompt needed (already active)
    //
    // Scenario #10: Remove permission via context menu
    //   User Action: Right-click extension -> Remove permission
    //   Expected: 1) onRemoved listener fires
    //             2) Unregister persistent script
    //             3) Find all matching tabs
    //             4) Prompt each tab to reload (cleanup)
    //
    // ==================================================================================

    chrome.permissions.onAdded.addListener(async (perm: chrome.permissions.Permissions) => {
        console.debug(`app.ts: ${_logTag} onAdded event - permissions added:`, perm.origins);
        const origins = perm?.origins || [];

        for (const matchPattern of origins) {
            try {
                // matchPattern looks like "https://example.com/*" or "http://localhost:8080/*"
                console.debug(`app.ts: ${_logTag} Processing added permission for: ${matchPattern}`);

                // Extract host-with-port for script ID (includes port for granular tracking)
                const hostWithPort = hostWithPortFromPattern(matchPattern);
                if (!hostWithPort) {
                    console.error(`app.ts: ${_logTag} Invalid match pattern: ${matchPattern}`);
                    continue;
                }

                const scriptId = scriptIdFromHostWithPort(hostWithPort);

                // Check if persistent content script is already registered
                // Branch point: Scenario #7 vs #8/#9
                const alreadyRegistered = await chrome.scripting.getRegisteredContentScripts({ ids: [scriptId] });
                if (alreadyRegistered.length > 0) {
                    // Scenario #8 or #9: Script already registered
                    console.debug(`app.ts: ${_logTag} Persistent content script already registered for ${hostWithPort}`);
                    // Continue to check tabs - may need reload if CS not active
                } else {
                    // Scenario #7: Need to register the script
                    await chrome.scripting.registerContentScripts([{
                        id: scriptId,
                        js: ["scripts/esbuild/ContentScript.js"],
                        matches: [matchPattern],
                        runAt: "document_idle",
                        allFrames: true,
                        world: "ISOLATED",
                        persistAcrossSessions: true,
                    }]);
                    console.debug(`app.ts: ${_logTag} Registered persistent content script for ${hostWithPort}`);
                }

                // Find all tabs matching this origin and check if they need reload
                // Implements: Scenario #7, #8, #9 - determine if reload prompt needed per tab
                const tabs = await chrome.tabs.query({ url: matchPattern });
                console.debug(`app.ts: ${_logTag} Found ${tabs.length} tabs matching ${matchPattern}`);

                for (const tab of tabs) {
                    if (!tab.id) continue;

                    try {
                        // Check if content script is already active in this tab
                        const pingResponse = await chrome.tabs.sendMessage(tab.id, { type: 'ping' });
                        if (pingResponse?.ok) {
                            // Scenario #9: Content script already active - no reload needed
                            console.debug(`app.ts: ${_logTag} Content script already active in tab ${tab.id}`);
                            await updateIconForTab(tab.id, true);
                            continue;
                        }
                    } catch (error) {
                        // Scenario #7 or #8: No content script active - prompt user to reload
                        console.debug(`app.ts: ${_logTag} No content script in tab ${tab.id}, prompting for reload...`);
                        await updateIconForTab(tab.id, false);

                        try {
                            await promptAndReloadTab(
                                tab.id,
                                `${PRODUCT_NAME} is now enabled for this site.\n\nReload the page to activate?`,
                                'onAdded'
                            );
                        } catch (promptError) {
                            console.error(`app.ts: ${_logTag} Error prompting for reload in tab ${tab.id}:`, promptError);
                        }
                    }
                }
            } catch (error) {
                console.error(`app.ts: ${_logTag} Error processing added permission for ${matchPattern}:`, error);
            }
        }
    });

    chrome.permissions.onRemoved.addListener(async (perm: chrome.permissions.Permissions) => {
        // Implements: Scenario #10 - Remove permission and clean up
        console.debug(`app.ts: ${_logTag} onRemoved event - permissions removed:`, perm.origins);
        const origins = perm?.origins || [];

        for (const matchPattern of origins) {
            console.debug(`app.ts: ${_logTag} Processing removed permission for: ${matchPattern}`);

            // Scenario #10: Unregister the content script for this match pattern
            await unregisterForPattern(matchPattern);

            // Find all tabs that match the removed origin and prompt for reload
            // This ensures previously injected content scripts are removed from pages
            try {
                const tabs = await chrome.tabs.query({ url: matchPattern });
                console.debug(`app.ts: ${_logTag} Found ${tabs.length} tabs matching ${matchPattern} for removal`);

                for (const tab of tabs) {
                    if (tab.id) {
                        await updateIconForTab(tab.id, false);

                        // Inject a prompt to reload the page (content script needs to be removed)
                        try {
                            await promptAndReloadTab(
                                tab.id,
                                `${PRODUCT_NAME} permissions were removed for this site.\n\nReload the page to complete the removal?`,
                                'onRemoved'
                            );
                        } catch (error) {
                            // Tab may have already been closed or navigated away
                            console.debug(`app.ts: ${_logTag} Could not prompt tab ${tab.id} for reload:`, error);
                        }
                    }
                }
            } catch (error) {
                console.error(`app.ts: ${_logTag} Error querying tabs for removed origin:`, matchPattern, error);
            }
        }
    });

    // ==================================================================================
    // CONTENT SCRIPT READY HANDLER
    // ==================================================================================
    //
    // Content scripts send a 'cs-ready' message when they initialize.
    // This handles the race condition where the service worker starts before
    // content scripts are ready (e.g., after browser restart with restored tabs).
    // The content script announces itself, and we update the icon accordingly.
    //
    // ==================================================================================

    chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
        console.debug(`app.ts: ${_logTag} onMessage received:`, message?.t ?? message?.type ?? 'unknown', 'from', sender?.url ?? sender?.tab?.id ?? 'unknown');

        // Handle CLIENT_SW_WAKE: App/CS is probing BW readiness.
        // Holds the response until WASM is fully loaded (afterStarted resolved _wasmReady).
        if (message?.t === 'CLIENT_SW_WAKE') {
            const isExtPage = sender?.url?.startsWith('chrome-extension://') === true;
            const source = isExtPage ? 'extension page' : `CS tab ${sender.tab?.id}`;
            console.debug(`app.ts: ${_logTag} CLIENT_SW_WAKE from ${source}, waiting for WASM...`);

            _wasmReady.then(() => {
                console.debug(`app.ts: ${_logTag} CLIENT_SW_WAKE from ${source}, replying awake=true`);
                const reply = { t: 'SW_CLIENT_AWAKE', ready: true };
                // sendResponse delivers the reply to the sender's sendMessage() Promise
                // (used by ContentScript which awaits the response directly).
                sendResponse(reply);
                // Broadcast to all extension pages so App's onMessage listener
                // sets __keriauth_bwReady = true (App uses InvokeVoidAsync and
                // ignores the sendResponse value).
                chrome.runtime.sendMessage(reply);
            });
            return true;  // keep message channel open for async sendResponse
        }

        // Note: 'cs-ready' is defined in CsInternalMsgEnum.CS_READY (@keriauth/types)
        // We use the literal here to avoid adding build dependencies to app.ts
        if (message?.type === 'cs-ready') {
            const tabId = sender.tab?.id;
            if (tabId) {
                console.log(`app.ts: ${_logTag} Content script ready in tab ${tabId}`);
                updateIconForTab(tabId, true);
            }
            return false; // No async response needed
        }
        // Let other listeners handle other message types
        return false;
    });

    // ==================================================================================
    // EXTENSION INSTALL/UPDATE/RELOAD HANDLER
    // ==================================================================================

    chrome.runtime.onInstalled.addListener(async (details) => {
        console.debug(`app.ts: ${_logTag} onInstalled event:`, details.reason);

        // On extension install, update, or reload, initialize the active tab's icon state.
        // We intentionally only handle the active tab to maintain minimal permissions.
        // Other tabs will have their icons updated when the user switches to them
        // (via onActivated) or when they navigate (via onUpdated).
        await initializeActiveTabState();
    });

    // ==================================================================================
    // SERVICE WORKER STARTUP INITIALIZATION
    // ==================================================================================
    //
    // When the service worker starts, we need to initialize icon state for the active tab.
    // This handles cases where:
    // - Extension was reloaded/reinstalled (content script context invalidated)
    // - Browser was restarted or resumed from suspend
    // - Service worker was awakened from idle
    //
    // Without this initialization, the icon would remain in default state until
    // the user switches tabs or navigates, which is confusing UX.
    //
    // ==================================================================================

    // Log registered content scripts at startup for debugging
    chrome.scripting.getRegisteredContentScripts().then(scripts => {
        console.debug(`app.ts: ${_logTag} Registered content scripts at startup:`, scripts.length, scripts.map(s => s.id));
    }).catch(err => {
        console.error(`app.ts: ${_logTag} Error getting registered content scripts at startup:`, err);
    });

    // Run initialization asynchronously (don't block module evaluation)
    initializeActiveTabState();
}

/**
 * Called before Blazor starts.
 * This hook loads JavaScript ES modules into the browser's module cache for the current runtime context.
 * Each Blazor runtime (BackgroundWorker and App) runs this separately with its own module cache.
 *
 * @param options Blazor WebAssembly start options. Refer to https://github.com/dotnet/aspnetcore/blob/main/src/Components/Web.JS/src/Platform/WebAssemblyStartOptions.ts
 * @param extensions Extensions added during publishing
 * @param blazorBrowserExtension Blazor browser extension instance
 */
export async function beforeStart(
    options: WebAssemblyStartOptions,
    extensions: Record<string, unknown>,
    blazorBrowserExtension: BrowserExtensionInstance
): Promise<void> {
    const startTime = Date.now();
    console.debug(`app.ts: ${_logTag} beforeStart - ENTRY`, {
        mode: blazorBrowserExtension.BrowserExtension.Mode,
        timestamp: startTime,
        extensionId: chrome.runtime.id
    });

    const mode = blazorBrowserExtension.BrowserExtension.Mode;

    // JavaScript ES modules are statically imported at the top of this file
    // This works in both ServiceWorker (Background) and standard (App) contexts
    // The modules are now available to C# code via IJSRuntime.InvokeAsync("import", path)
    console.debug(`app.ts: ${_logTag} Modules loaded via static imports:`, Object.keys((globalThis as any).appModules));

    // NOTE: signifyClient is NOT preloaded here because:
    // 1. libsodium WASM initializes during ES module evaluation (not in a function)
    // 2. This happens DURING the import() call, before any polyfill code in the module can run
    // 3. The error occurs before the module's banner or any code executes
    //
    // SOLUTION: signifyClient will be lazy-loaded by C# services via IJSRuntime.InvokeAsync("import", ...)
    // when first needed. By that time, Blazor and all crypto APIs are guaranteed to be fully available.
    // The polyfill in signifyClient.js banner will run first and libsodium will initialize successfully.
    console.debug(`app.ts: ${_logTag} signifyClient will be lazy-loaded by C# when needed`);

    switch (mode) {
        case 'Background':
            // All Background-mode chrome.* event listeners are registered at module level
            // (above beforeStart) for cold-start resilience. See the if (_isSW) block.
            console.debug(`app.ts: ${_logTag} Background mode — listeners already registered at module level`);
            break;
        case 'Standard':
        case 'Debug':
            // Use 'window' (not globalThis) so Blazor's IJSRuntime can resolve identifiers.

            // TODO P3 rename __keriauth_* globals to use PRODUCT_NAME-based prefix
            // BW readiness flag: set when SW_CLIENT_AWAKE arrives
            (window as any).__keriauth_bwReady = false;
            (window as any).__keriauth_isBwReady = () => (window as any).__keriauth_bwReady === true;

            // Wake signal flag: set when SW_APP_WAKE arrives, reset on read
            (window as any).__keriauth_appWake = false;
            (window as any).__keriauth_checkAppWake = () => {
                const wake = (window as any).__keriauth_appWake === true;
                if (wake) (window as any).__keriauth_appWake = false;
                return wake;
            };

            chrome.runtime.onMessage.addListener((message) => {
                if (message?.t === 'SW_CLIENT_AWAKE' && message?.ready === true) {
                    (window as any).__keriauth_bwReady = true;
                    console.log(`app.ts: ${_logTag} Received SW_CLIENT_AWAKE, BW is awake`);
                }
                if (message?.t === 'SW_APP_WAKE') {
                    (window as any).__keriauth_appWake = true;
                    console.debug(`app.ts: ${_logTag} Received SW_APP_WAKE, requestId=`, message?.requestId);
                }
                return false;
            });
            console.debug(`app.ts: ${_logTag} ${mode} mode — registered onMessage listeners`);
            break;
        default:
            console.warn(`app.ts: ${_logTag} Unknown mode: ${mode}`);
            break;
    }
    console.debug(`app.ts: ${_logTag} beforeStart completed`);
    return;
}

/**
 * Called after Blazor is ready to receive calls from JS.
 * @param blazor The Blazor instance
 */
export function afterStarted(blazor: unknown): void {
    console.debug(`app.ts: ${_logTag} afterStarted - Blazor runtime ready`);
    if (_isSW) {
        _wasmReadyResolve();
        console.debug(`app.ts: ${_logTag} afterStarted - _wasmReady resolved, CLIENT_SW_WAKE will now reply immediately`);
    }
}
