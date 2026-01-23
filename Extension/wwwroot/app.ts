/// <reference types="chrome-types" />
/*
 * For details, see https://mingyaulee.github.io/Blazor.BrowserExtension/app-js
 */

// CRITICAL: Import libsodium polyfill FIRST, before any other module imports
// This ensures all crypto polyfills are in place before libsodium WASM initializes
// See commits 90aa450 and 6f33fba for details
import './scripts/es6/libsodium-polyfill.js';

// Polyfills for libsodium WASM initialization
//
// 1. Polyfill global object (service workers only have 'self', not 'global' or 'window')
// This must be set BEFORE any module imports libsodium
if (typeof self !== 'undefined' && typeof (globalThis as any).global === 'undefined') {
    (globalThis as any).global = self;
    console.log('app.ts: Polyfilled global object for libsodium');
}

// Note: crypto.randomBytes, window.crypto fix, and Module.getRandomValue
// are now handled by libsodium-polyfill.js imported above

// Static imports - work in both ServiceWorker (backgroundWorker) and standard contexts
// Import modules with names so they're registered in the module system
// NOTE: signifyClient and demo1 are NOT statically imported here because:
// - signifyClient: contains libsodium which needs crypto APIs ready first
// - demo1: has TypeScript errors that would fail compilation (loaded via JsModuleLoader instead)
// NOTE: WebAuthn modules (navigatorCredentialsShim, aesGcmCrypto) are loaded via JsModuleLoader
// and cached by browser module system - no static import needed here
// import * as signifyClient from './scripts/esbuild/signifyClient.js';
// import * as utils from './scripts/esbuild/utils.js';
// import * as demo1 from './scripts/esbuild/demo1.js';

// Make modules available globally for debugging
(globalThis as any).appModules = {};  // WebAuthn modules loaded dynamically by JsModuleLoader

const CS_ID_PREFIX = 'keriauth-cs';

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
    console.log('app.ts: beforeStart', { options, extensions, blazorBrowserExtension });

    const mode = blazorBrowserExtension.BrowserExtension.Mode;

    // JavaScript ES modules are statically imported at the top of this file
    // This works in both ServiceWorker (Background) and standard (App) contexts
    // The modules are now available to C# code via IJSRuntime.InvokeAsync("import", path)
    console.log('app.ts: Modules loaded via static imports:', Object.keys((globalThis as any).appModules));

    // NOTE: signifyClient is NOT preloaded here because:
    // 1. libsodium WASM initializes during ES module evaluation (not in a function)
    // 2. This happens DURING the import() call, before any polyfill code in the module can run
    // 3. The error occurs before the module's banner or any code executes
    //
    // SOLUTION: signifyClient will be lazy-loaded by C# services via IJSRuntime.InvokeAsync("import", ...)
    // when first needed. By that time, Blazor and all crypto APIs are guaranteed to be fully available.
    // The polyfill in signifyClient.js banner will run first and libsodium will initialize successfully.
    console.log('app.ts: signifyClient will be lazy-loaded by C# when needed');

    switch (mode) {
        case 'Background':
            console.log('app.ts: Setting up Background mode event handlers');

            /**
             * Note that JS imports for backgroundWorker requires static module loading via import statements at the top of the js files or declaration in the .csproj file. See https://mingyaulee.github.io/Blazor.BrowserExtension/background-worker
            */

            /**
             * Generates match patterns from a tab URL for port-specific permission tracking.
             * @param tabUrl The full URL of the tab
             * @returns Array of match patterns (e.g., ["http://foo.example.com:8080/*"])
             */
            const matchPatternsFromTabUrl = (tabUrl: string): string[] => {
                try {
                    const u = new URL(tabUrl);
                    if (u.protocol === "http:" || u.protocol === "https:") {
                        // Exact host with port (if specified):
                        // This creates port-specific permissions for granular security control
                        // e.g., http://foo.example.com:8080/* vs http://example.com/*
                        return [`${u.protocol}//${u.host}/*`];
                    }
                } catch (e) {
                    // file:///*, about:blank, data:, chrome://, chrome-extension://, etc.
                }
                return [];
            };

            /**
             * Extracts host-with-port identifier from a match pattern.
             * @param matchPattern Pattern like "http://foo.example.com:8080/*"
             * @returns Host with port like "foo.example.com:8080" or empty string if invalid
             */
            const hostWithPortFromPattern = (matchPattern: string): string => {
                try {
                    // Remove trailing "/*" from pattern
                    const urlString = matchPattern.slice(0, matchPattern.length - 2);
                    const u = new URL(urlString);
                    return u.host; // Returns hostname:port or just hostname if port is default
                } catch (e) {
                    console.error(`app.ts: Invalid match pattern: ${matchPattern}`);
                    return '';
                }
            };

            /**
             * Generates a content script ID from host-with-port.
             * @param hostWithPort Host with port like "foo.example.com:8080"
             * @returns Script ID like "keriauth-cs-foo.example.com:8080"
             */
            const scriptIdFromHostWithPort = (hostWithPort: string): string => {
                return `${CS_ID_PREFIX}-${hostWithPort}`;
            };

            /**
             * Unregisters content script for a given match pattern.
             * @param matchPattern Pattern like "http://foo.example.com:8080/*"
             */
            const unregisterForPattern = async (matchPattern: string): Promise<void> => {
                const hostWithPort = hostWithPortFromPattern(matchPattern);
                if (!hostWithPort) {
                    console.error(`app.ts: Cannot unregister - invalid pattern: ${matchPattern}`);
                    return;
                }
                const scriptId = scriptIdFromHostWithPort(hostWithPort);
                try {
                    await chrome.scripting.unregisterContentScripts({ ids: [scriptId] });
                    console.log(`app.ts: Unregistered content script: ${scriptId}`);
                } catch (e) {
                    // Script may not exist, ignore error
                    console.log(`app.ts: Script ${scriptId} not found for unregistration (may not exist)`);
                }
            };

            /**
             * Prompts user to reload a tab and optionally reloads if accepted.
             * @param tabId The ID of the tab to prompt
             * @param message The confirmation message to display
             * @param context Description of where this prompt is being called from (for logging)
             * @returns true if user accepted and reload was triggered, false otherwise
             */
            const promptAndReloadTab = async (tabId: number, message: string, context: string): Promise<boolean> => {
                try {
                    const reloadResponse = await chrome.scripting.executeScript({
                        target: { tabId },
                        func: (msg: string) => confirm(msg),
                        args: [message]
                    } as any);

                    const userAccepted = reloadResponse?.[0]?.result === true;

                    if (userAccepted) {
                        console.log(`app.ts: User accepted reload prompt (${context}) - reloading tab ${tabId}`);
                        await chrome.tabs.reload(tabId);
                        return true;
                    } else {
                        console.log(`app.ts: User declined reload prompt (${context}) for tab ${tabId}`);
                        return false;
                    }
                } catch (error) {
                    console.error(`app.ts: ERROR in promptAndReloadTab (${context}) for tab ${tabId}:`, error);
                    throw error;
                }
            };

            /**
             * Updates the extension toolbar icon for a specific tab based on content script state.
             * @param tabId The tab to update the icon for
             * @param isActive true if content script is active, false to reset to default inactive icon
             */
            const updateIconForTab = async (tabId: number, isActive: boolean): Promise<void> => {
                const iconPrefix = isActive ? "logo" : "logoB";

                try {
                    await chrome.action.setIcon({
                        path: {
                            16: chrome.runtime.getURL(`images/${iconPrefix}016.png`),
                            32: chrome.runtime.getURL(`images/${iconPrefix}032.png`),
                            48: chrome.runtime.getURL(`images/${iconPrefix}048.png`),
                            128: chrome.runtime.getURL(`images/${iconPrefix}128.png`)
                        },
                        tabId
                    });
                    // await chrome.action.setBadgeBackgroundColor({ color: isActive ? '#0F9D58' : '#808080', tabId });
                    if (isActive) {
                        // await chrome.action.setBadgeText({ text: 'ON', tabId });
                    } else {
                        // await chrome.action.setBadgeText({ text: 'off', tabId });
                    }
                    console.log(`app.ts: Set ${isActive ? 'active' : 'inactive'} icon for tab ${tabId}`);
                } catch (error) {
                    console.warn(`app.ts: Failed to set ${isActive ? 'active' : 'inactive'} icon for tab ${tabId}:`, error);
                }
            };

            // ==================================================================================
            // TAB EVENT LISTENERS FOR ICON UPDATES
            // ==================================================================================

            /**
             * Checks if a tab URL could have a content script registered.
             * Only http/https URLs are supported for content script injection.
             * @param url The tab URL to check
             * @returns true if the URL scheme supports content scripts
             */
            const isSupportedUrlScheme = (url: string | undefined): boolean => {
                if (!url) return false;
                try {
                    const u = new URL(url);
                    return u.protocol === 'http:' || u.protocol === 'https:';
                } catch {
                    return false;
                }
            };

            /**
             * Checks if there's a registered content script that matches the given URL.
             * @param url The URL to check against registered content scripts
             * @returns true if a content script is registered for this URL's origin
             */
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

            /**
             * Opens the action popup if no popup or sidepanel is already open.
             * Must be called with a user gesture context (e.g., from action click handler).
             */
            const openPopupIfNotOpen = async (): Promise<void> => {
                try {
                    // Check if popup or sidepanel is already open
                    const contexts = await chrome.runtime.getContexts({
                        contextTypes: ['POPUP', 'SIDE_PANEL']
                    });

                    if (contexts.length > 0) {
                        console.log("app.ts: Popup or sidepanel already open, skipping openPopup");
                        return;
                    }

                    // Set the popup URL (required before openPopup when no default_popup in manifest)
                    await chrome.action.setPopup({ popup: '/indexInPopup.html' });

                    // Open the popup
                    try {
                        await chrome.action.openPopup();
                        console.log("app.ts: Popup opened successfully");
                    } catch (error) {
                        // openPopup() sometimes throws even on success
                        console.debug("app.ts: openPopup() threw (may still have succeeded):", error);
                    }

                    // Clear the popup setting so future clicks trigger onClicked event
                    await chrome.action.setPopup({ popup: '' });

                } catch (error) {
                    console.error("app.ts: Error in openPopupIfNotOpen:", error);
                }
            };

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
                try {
                    if (!tab?.id || !tab.url) return;

                    // 1) Compute per-origin match patterns from the clicked tab
                    const MATCHES = matchPatternsFromTabUrl(tab.url);
                    if (MATCHES.length === 0) {
                        console.log("app.ts: Unsupported or restricted URL scheme; not registering persistence.", tab.url);
                        return;
                    }

                    // 2) Request persistent host permission
                    // CRITICAL: request() must be the VERY FIRST async operation to preserve user gesture
                    // Note: chrome.permissions.request() returns true immediately if permission already exists
                    // (no prompt shown to user in that case)
                    // Implements: Scenario #1, #2, #6 (permission handling)
                    const wanted = { origins: MATCHES };
                    try {
                        const granted = await chrome.permissions.request(wanted);
                        console.log("app.ts: Permission request completed, granted:", granted);
                        if (!granted) {
                            // Scenario #6: User declined permission
                            console.log("app.ts: User declined persistent host permission; stopping.");
                            return;
                        }
                        console.log("app.ts: Permission granted (or was already granted)");
                    } catch (error) {
                        console.error("app.ts: ERROR in chrome.permissions.request():", error);
                        throw error;
                    }

                    // 3) Check if persistent content script is already registered for this origin
                    // Implements: Branch point for Scenarios #4, #5 vs #1, #2, #3
                    const hostWithPort = hostWithPortFromPattern(MATCHES[0] || '');
                    const scriptId = scriptIdFromHostWithPort(hostWithPort);
                    let already: chrome.scripting.RegisteredContentScript[] = [];
                    try {
                        already = await chrome.scripting.getRegisteredContentScripts({ ids: [scriptId] });
                        console.log("app.ts: Script registration check completed, found:", already.length);
                    } catch (error) {
                        console.error("app.ts: ERROR in chrome.scripting.getRegisteredContentScripts():", error);
                        throw error;
                    }
                    if (already.length > 0) {
                        // Script is already registered - check if it's active or stale
                        // Implements: Scenario #4 (active) or Scenario #5 (stale)
                        console.log("app.ts: Persistent content script already registered");

                        // Check if there's a stale content script from a previous extension installation
                        // This can happen when extension is uninstalled/reinstalled and page wasn't reloaded
                        try {
                            const pingResponse = await chrome.tabs.sendMessage(tab.id, { type: 'ping' });
                            if (pingResponse?.ok) {
                                // Scenario #4: Content script is active and responding
                                console.log("app.ts: Content script is active and responding");
                                await updateIconForTab(tab.id, true);
                                // Open popup for user interaction (if not already open)
                                await openPopupIfNotOpen();
                                return;
                            }
                        } catch (error) {
                            // Scenario #5: Content script registered but not responding (stale)
                            console.warn("app.ts: Content script registered but not responding - may need reload");
                            await updateIconForTab(tab.id, false);
                            await promptAndReloadTab(
                                tab.id,
                                'KERI Auth needs to refresh the connection with this page.\n\nReload to ensure proper functionality?',
                                'stale script'
                            );
                            return;
                        }
                    } else {
                        // Script NOT registered yet - Implements: Scenario #1, #2, or #3
                        console.log("app.ts: No persistent content script registered yet - proceeding", { MATCHES, scriptId });

                        // 4) Check if content script is already active in the page
                        // This can happen if permission was removed and then re-added on same tab
                        // The content script stays in the page even after permission removal
                        // Branch point: Scenario #3 (CS active) vs Scenario #1/#2 (CS not active)
                        let contentScriptAlreadyActive = false;
                        try {
                            const pingResponse = await chrome.tabs.sendMessage(tab.id, { type: 'ping' });
                            if (pingResponse?.ok) {
                                contentScriptAlreadyActive = true;
                                console.log("app.ts: Content script already active (likely permission re-grant)");
                            }
                        } catch (error) {
                            console.log("app.ts: No active content script detected");
                        }

                        // Scenario #3: Content script already in page (permission re-grant case)
                        if (contentScriptAlreadyActive) {
                            console.log("app.ts: Skipping one-shot injection, registering persistent script and prompting reload");

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
                                console.log("app.ts: Registered persistent content script:", scriptId);
                            } catch (error) {
                                console.error("app.ts: ERROR in chrome.scripting.registerContentScripts() [contentScriptAlreadyActive branch]:", error);
                                throw error;
                            }

                            await updateIconForTab(tab.id, true);

                            // Strongly recommend reload to clear any stale state
                            await promptAndReloadTab(
                                tab.id,
                                'KERI Auth is now enabled for this site.\n\nReload the page to ensure clean state?',
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
                            console.log("app.ts: One-shot injection completed successfully");
                        } catch (error) {
                            console.error("app.ts: ERROR in chrome.scripting.executeScript() [one-shot injection]:", error);
                            throw error;
                        }

                        if (!oneShotResult?.[0]?.documentId) {
                            console.error("app.ts: One-shot injection failed.", oneShotResult);
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
                            console.log("app.ts: Registered persistent content script:", scriptId, "for", MATCHES);
                        } catch (error: any) {
                            // Handle race condition: onAdded listener may have already registered the script
                            // when permission was just granted via the permission prompt in Scenario #1
                            // This is expected behavior - we continue to the reload prompt regardless
                            if (error?.message?.includes('Duplicate script ID')) {
                                console.log("app.ts: Script already registered (likely by onAdded listener) - continuing to reload prompt");
                            } else {
                                console.error("app.ts: ERROR in chrome.scripting.registerContentScripts() [main flow]:", error);
                                throw error;
                            }
                        }

                        // 7) Prompt user to reload page to activate persistent content script
                        // Implements: Final step of Scenario #1 & #2 - user must reload to activate persistent script
                        await promptAndReloadTab(
                            tab.id,
                            'KERI Auth is now enabled for this site.\n\nReload the page to activate?',
                            'main flow'
                        );
                    }
                } catch (error: any) {
                    console.error('app.ts: Error in action.onClicked handler:', error);
                }
            });

            // ==================================================================================
            // CONTEXT MENU PERMISSION CHANGE HANDLERS
            // ==================================================================================
            //
            // REQUIREMENT SCENARIOS (Permission Added via Context Menu):
            //
            // Scenario #7: Grant permission via context menu (No Script, No Active CS)
            //   User Action: Right-click extension → "When you click the extension" or "On this site"
            //   Expected: 1) onAdded listener fires
            //             2) Register persistent script
            //             3) Find all matching tabs
            //             4) Prompt each tab to reload
            //
            // Scenario #8: Grant permission via context menu (Script Already Registered, No Active CS)
            //   User Action: Right-click extension → Grant permission
            //   Expected: 1) onAdded fires
            //             2) Detect script already registered - skip registration
            //             3) Find matching tabs without active CS
            //             4) Prompt those tabs to reload
            //
            // Scenario #9: Grant permission via context menu (Script Registered, Active CS Exists)
            //   User Action: Right-click extension → Grant permission
            //   Expected: 1) onAdded fires
            //             2) Detect script already registered - skip
            //             3) Ping tabs - content script responds
            //             4) No reload prompt needed (already active)
            //
            // Scenario #10: Remove permission via context menu
            //   User Action: Right-click extension → Remove permission
            //   Expected: 1) onRemoved listener fires
            //             2) Unregister persistent script
            //             3) Find all matching tabs
            //             4) Prompt each tab to reload (cleanup)
            //
            // ==================================================================================

            chrome.permissions.onAdded.addListener(async (perm: chrome.permissions.Permissions) => {
                console.log('app.ts: onAdded event - permissions added:', perm.origins);
                const origins = perm?.origins || [];

                for (const matchPattern of origins) {
                    try {
                        // matchPattern looks like "https://example.com/*" or "http://localhost:8080/*"
                        console.log(`app.ts: Processing added permission for: ${matchPattern}`);

                        // Extract host-with-port for script ID (includes port for granular tracking)
                        const hostWithPort = hostWithPortFromPattern(matchPattern);
                        if (!hostWithPort) {
                            console.error(`app.ts: Invalid match pattern: ${matchPattern}`);
                            continue;
                        }

                        const scriptId = scriptIdFromHostWithPort(hostWithPort);

                        // Check if persistent content script is already registered
                        // Branch point: Scenario #7 vs #8/#9
                        const alreadyRegistered = await chrome.scripting.getRegisteredContentScripts({ ids: [scriptId] });
                        if (alreadyRegistered.length > 0) {
                            // Scenario #8 or #9: Script already registered
                            console.log(`app.ts: Persistent content script already registered for ${hostWithPort}`);
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
                            console.log(`app.ts: Registered persistent content script for ${hostWithPort}`);
                        }

                        // Find all tabs matching this origin and check if they need reload
                        // Implements: Scenario #7, #8, #9 - determine if reload prompt needed per tab
                        const tabs = await chrome.tabs.query({ url: matchPattern });
                        console.log(`app.ts: Found ${tabs.length} tabs matching ${matchPattern}`);

                        for (const tab of tabs) {
                            if (!tab.id) continue;

                            try {
                                // Check if content script is already active in this tab
                                const pingResponse = await chrome.tabs.sendMessage(tab.id, { type: 'ping' });
                                if (pingResponse?.ok) {
                                    // Scenario #9: Content script already active - no reload needed
                                    console.log(`app.ts: Content script already active in tab ${tab.id}`);
                                    await updateIconForTab(tab.id, true);
                                    continue;
                                }
                            } catch (error) {
                                // Scenario #7 or #8: No content script active - prompt user to reload
                                console.log(`app.ts: No content script in tab ${tab.id}, prompting for reload...`);
                                await updateIconForTab(tab.id, false);

                                try {
                                    await promptAndReloadTab(
                                        tab.id,
                                        'KERI Auth is now enabled for this site.\n\nReload the page to activate?',
                                        'onAdded'
                                    );
                                } catch (promptError) {
                                    console.error(`app.ts: Error prompting for reload in tab ${tab.id}:`, promptError);
                                }
                            }
                        }
                    } catch (error) {
                        console.error(`app.ts: Error processing added permission for ${matchPattern}:`, error);
                    }
                }
            });

            chrome.permissions.onRemoved.addListener(async (perm: chrome.permissions.Permissions) => {
                // Implements: Scenario #10 - Remove permission and clean up
                console.log('app.ts: onRemoved event - permissions removed:', perm.origins);
                const origins = perm?.origins || [];

                for (const matchPattern of origins) {
                    console.log(`app.ts: Processing removed permission for: ${matchPattern}`);

                    // Scenario #10: Unregister the content script for this match pattern
                    await unregisterForPattern(matchPattern);

                    // Find all tabs that match the removed origin and prompt for reload
                    // This ensures previously injected content scripts are removed from pages
                    try {
                        const tabs = await chrome.tabs.query({ url: matchPattern });
                        console.log(`app.ts: Found ${tabs.length} tabs matching ${matchPattern} for removal`);

                        for (const tab of tabs) {
                            if (tab.id) {
                                await updateIconForTab(tab.id, false);

                                // Inject a prompt to reload the page (content script needs to be removed)
                                try {
                                    await promptAndReloadTab(
                                        tab.id,
                                        'KERI Auth permissions were removed for this site.\n\nReload the page to complete the removal?',
                                        'onRemoved'
                                    );
                                } catch (error) {
                                    // Tab may have already been closed or navigated away
                                    console.log(`app.ts: Could not prompt tab ${tab.id} for reload:`, error);
                                }
                            }
                        }
                    } catch (error) {
                        console.error('app.ts: Error querying tabs for removed origin:', matchPattern, error);
                    }
                }
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
                    console.log(`app.ts: No registered content script for ${tabUrl}`);
                    await updateIconForTab(tabId, false);
                    return;
                }

                console.log(`app.ts: Found registered content script for ${tabUrl}, pinging...`);

                // Try to ping the content script
                try {
                    const pingResponse = await chrome.tabs.sendMessage(tabId, { type: 'ping' });
                    const isActive = pingResponse?.ok === true;
                    console.log(`app.ts: Content script ping result for tab ${tabId}: ${isActive ? 'active' : 'not responding'}`);
                    await updateIconForTab(tabId, isActive);
                } catch {
                    // Content script registered but not responding
                    // This is expected after extension reload - CS context is invalidated
                    console.log(`app.ts: Content script registered but not responding for tab ${tabId} (may need page reload)`);
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
                        console.log('app.ts: No active tab found during initialization');
                        return;
                    }

                    console.log(`app.ts: Initializing state for active tab ${activeTab.id}: ${activeTab.url}`);
                    await initializeTabIconState(activeTab.id, activeTab.url);
                } catch (error) {
                    console.error('app.ts: Error during active tab initialization:', error);
                }
            };

            // Run initialization asynchronously (don't block beforeStart completion)
            initializeActiveTabState();

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

            chrome.runtime.onMessage.addListener((message, sender, _sendResponse) => {
                // Note: 'cs-ready' is defined in CsInternalMsgEnum.CS_READY (@keriauth/types)
                // We use the literal here to avoid adding build dependencies to app.ts
                if (message?.type === 'cs-ready') {
                    const tabId = sender.tab?.id;
                    if (tabId) {
                        console.log(`app.ts: Content script ready in tab ${tabId}`);
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
                console.log('app.ts: onInstalled event:', details.reason);

                // On extension install, update, or reload, initialize the active tab's icon state.
                // We intentionally only handle the active tab to maintain minimal permissions.
                // Other tabs will have their icons updated when the user switches to them
                // (via onActivated) or when they navigate (via onUpdated).
                await initializeActiveTabState();
            });
            break;
        default:
            console.log(`app.ts: Unknown mode: ${mode}`);
            break;
    }
    console.log('app.ts: beforeStart completed');
    return;
}

/**
 * Called after Blazor is ready to receive calls from JS.
 * @param blazor The Blazor instance
 */
export function afterStarted(blazor: unknown): void {
    console.log('app.ts: afterStarted - Blazor runtime ready');
    // Note: JavaScript modules are already loaded and cached by beforeStart() hook above
    // C# code can access them via IJSRuntime.InvokeAsync("import", path) which returns instantly from browser cache
}
