/// <reference types="chrome-types" />
/*
 * For details, see https://mingyaulee.github.io/Blazor.BrowserExtension/app-js
 */

// Polyfills for libsodium WASM initialization
//
// 1. Polyfill global object (service workers only have 'self', not 'global' or 'window')
// This must be set BEFORE any module imports libsodium
if (typeof self !== 'undefined' && typeof (globalThis as any).global === 'undefined') {
    (globalThis as any).global = self;
    console.log('app.ts: Polyfilled global object for libsodium');
}

// 2. Polyfill crypto.randomBytes for libsodium
// Note: In service workers, self.crypto is read-only and already exists.
// We need to add the randomBytes() method using Object.defineProperty
// to avoid "Cannot set property" errors on read-only objects.
if (typeof self !== 'undefined' && self.crypto && !(self.crypto as any).randomBytes) {
    try {
        Object.defineProperty(self.crypto, 'randomBytes', {
            value: (size: number) => {
                const buffer = new Uint8Array(size);
                self.crypto.getRandomValues(buffer);
                return buffer;
            },
            writable: false,
            configurable: true
        });
        console.log('app.ts: Polyfilled crypto.randomBytes for libsodium');
    } catch (e) {
        console.error('app.ts: Failed to polyfill crypto.randomBytes:', e);
    }
}

// Static imports - work in both ServiceWorker and standard contexts
// Import modules with names so they're registered in the module system
// NOTE: signifyClient is NOT statically imported because it contains libsodium
// which needs crypto APIs to be ready first
import * as storageHelper from './scripts/es6/storageHelper.js';
import * as webauthnCredentialWithPRF from './scripts/es6/webauthnCredentialWithPRF.js';

// Make modules available globally for debugging
(globalThis as any).appModules = {
    storageHelper,
    webauthnCredentialWithPRF
};

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

    if (mode === 'Background') {
        console.log('app.ts: Setting up Background mode event handlers');

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
                            // Scenario #4: Content script is active and responding - no action needed
                            console.log("app.ts: Content script is active and responding");
                            return;
                        }
                    } catch (error) {
                        // Scenario #5: Content script registered but not responding (stale)
                        console.warn("app.ts: Content script registered but not responding - may need reload");
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
                                continue;
                            }
                        } catch (error) {
                            // Scenario #7 or #8: No content script active - prompt user to reload
                            console.log(`app.ts: No content script in tab ${tab.id}, prompting for reload...`);

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

        chrome.runtime.onInstalled.addListener(async () => {
            // TODO P2: Implement migration/cleanup logic here
            // Optional: migration/cleanup if you change CS ids or structure between versions.
        });
    }
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
