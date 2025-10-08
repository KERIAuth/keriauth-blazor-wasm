/// <reference types="chrome-types" />
/*
 * For details, see https://mingyaulee.github.io/Blazor.BrowserExtension/app-js
 */

// Static imports - work in both ServiceWorker and standard contexts
// Import modules with names so they're registered in the module system
import * as signifyClient from './scripts/esbuild/signifyClient.js';
import * as storageHelper from './scripts/es6/storageHelper.js';
import * as permissionsHelper from './scripts/es6/PermissionsHelper.js';
import * as portMessageHelper from './scripts/es6/PortMessageHelper.js';
import * as swAppInterop from './scripts/es6/SwAppInterop.js';
import * as webauthnCredentialWithPRF from './scripts/es6/webauthnCredentialWithPRF.js';

// Make modules available globally for debugging
(globalThis as any).appModules = {
    signifyClient,
    storageHelper,
    permissionsHelper,
    portMessageHelper,
    swAppInterop,
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

    if (mode === 'Background') {
        console.log('app.ts: Setting up Background mode event handlers');

        function matchPatternsFromTabUrl(tabUrl: string): string[] {
            try {
                const u = new URL(tabUrl);
                if (u.protocol === "http:" || u.protocol === "https:") {
                    // Exact host only:
                    return [`${u.protocol}//${u.hostname}/*`];

                    // If you prefer to cover subdomains too, replace the above with a base-domain
                    // calculation and use: `${u.protocol}//*.${baseDomain}/*`
                    // (left out here to avoid false positives).
                }
            } catch (e) {
                // file:///*, about:blank, data:, chrome://, chrome-extension://, etc.
            }
            return [];
        }

        async function unregisterForOrigin(origin: string): Promise<void> {
            const id = CS_ID_PREFIX + origin;
            try {
                await chrome.scripting.unregisterContentScripts({ ids: [id] });
            } catch { /* ignore if already gone */ }
        }

        // Main extension icon click handler
        // Checks existing permissions first, only prompts if needed
        chrome.action.onClicked.addListener(async (tab: chrome.tabs.Tab) => {
            try {
                if (!tab?.id || !tab.url) return;

                // 1) Compute per-origin match patterns from the clicked tab
                const MATCHES = matchPatternsFromTabUrl(tab.url);
                if (MATCHES.length === 0) {
                    console.log("app.ts: Unsupported or restricted URL scheme; not registering persistence.", tab.url);
                    return;
                }

                // 2) Request persistent host permission FIRST (while user gesture is still active)
                // Note: request() must be the first async call to preserve the user gesture
                const wanted = { origins: MATCHES };
                const granted = await chrome.permissions.request(wanted);
                if (!granted) {
                    console.log("app.ts: User declined persistent host permission; stopping.");
                    return;
                }

                // 3) Check if persistent content script is already registered for this origin
                const scriptId = `${CS_ID_PREFIX}-${new URL(tab.url).hostname}`;
                const already = await chrome.scripting.getRegisteredContentScripts({ ids: [scriptId] });
                if (already.length > 0) {
                    console.log("app.ts: Persistent content script already registered");
                    return;
                } else {
                    console.log("app.ts: No persistent content script registered yet - proceeding", { MATCHES, scriptId });

                    // 4) Inject a one-shot script into the current page
                    const oneShotResult = await chrome.scripting.executeScript({
                        target: { tabId: tab.id },
                        files: ["scripts/esbuild/ContentScript.js"],
                        injectImmediately: false,
                        world: "ISOLATED",
                    });
                    // console.log("app.ts: One-shot injection result:", oneShotResult);
                    if (!oneShotResult?.[0]?.documentId) {
                        console.error("app.ts: One-shot injection failed.", oneShotResult);
                        return;
                    }

                    // 5) Register a persistent content script for this origin
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
                }

            } catch (error: any) {
                console.error('app.ts: Error in action.onClicked handler:', error);
            }
        });

        // Keep registrations in sync when permissions change:
        chrome.permissions.onAdded.addListener(async (perm: chrome.permissions.Permissions) => {
            const origins = perm?.origins || [];
            for (const pattern of origins) {
                // pattern looks like "https://example.com/*"
                const origin = pattern.slice(0, pattern.length - 1); // strip trailing '*'
                // TODO P2: Consider checking if already registered first
                // await ensureRegisteredForOrigin(origin.slice(0, -1)); // also remove trailing '/'
            }
        });

        chrome.permissions.onRemoved.addListener(async (perm: chrome.permissions.Permissions) => {
            const origins = perm?.origins || [];
            for (const pattern of origins) {
                const origin = pattern.slice(0, pattern.length - 1);
                await unregisterForOrigin(origin.slice(0, -1));
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
