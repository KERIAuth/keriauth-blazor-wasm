/*
* For details, see https://mingyaulee.github.io/Blazor.BrowserExtension/app-js
*/

const CS_ID_PREFIX = "keriauth-cs";

/**
 * Called before Blazor starts.
 * @param {object} options Blazor WebAssembly start options. Refer to https://github.com/dotnet/aspnetcore/blob/main/src/Components/Web.JS/src/Platform/WebAssemblyStartOptions.ts
 * @param {object} extensions Extensions added during publishing
 * @param {object} blazorBrowserExtension Blazor browser extension instance
 */
export function beforeStart(options, extensions, blazorBrowserExtension) {
    console.log("app.js: beforeStart", { options, extensions, blazorBrowserExtension });
    if (blazorBrowserExtension.BrowserExtension.Mode === 'Background') {
        console.log("app.js: Background mode detected");

        // Helper functions
        function originFromUrl(url) {
            try {
                const u = new URL(url);
                return `${u.protocol}//${u.host}`; // includes port if present
            } catch {
                return null;
            }
        }

        function isOriginGranted(origin) {
            return chrome.permissions.contains({ origins: [`${origin}/*`] });
        }

        // Uses .then() chains instead of async/await to preserve user gesture context
        // When called from chrome.permissions.request() callback, using await would break
        // the synchronous call chain and cause "must be called during a user gesture" error
        function ensureRegisteredForOrigin(origin) {
            const id = CS_ID_PREFIX + origin;
            return chrome.scripting.getRegisteredContentScripts({ ids: [id] })
                .catch((error) => {
                    console.log('app.js: Error checking registered scripts (will proceed with registration):', error);
                    return [];
                })
                .then(registered => {
                    if (registered && registered.length) {
                        console.log('app.js: Content script already registered for:', origin);
                        return; // already registered
                    }

                    return chrome.scripting.registerContentScripts([{
                        id,
                        matches: [`${origin}/*`],
                        js: ["scripts/esbuild/ContentScript.js"],
                        runAt: "document_start",
                        world: "ISOLATED",
                        allFrames: true
                    }]).then(() => {
                        console.log('app.js: Successfully registered content script for:', origin);
                    });
                });
        }

        async function unregisterForOrigin(origin) {
            const id = CS_ID_PREFIX + origin;
            try {
                await chrome.scripting.unregisterContentScripts({ ids: [id] });
            } catch { /* ignore if already gone */ }
        }

        // Try to ping the content script to avoid double-injection
        // Uses .then() to preserve user gesture context when called from click handler
        function isContentPresent(tabId) {
            return chrome.tabs.sendMessage(tabId, { type: "ping" })
                .then(resp => resp && resp.ok === true)
                .catch(() => false); // No receiver means not injected yet
        }

        // One-off manual injection into this tab (isolated world)
        // Uses .then() to preserve user gesture context when called from click handler
        function injectOnce(tabId) {
            return chrome.scripting.executeScript({
                target: { tabId, allFrames: false },
                files: ["scripts/esbuild/ContentScript.js"],
                world: "ISOLATED",
                injectImmediately: true
            });
        }

        // Main extension icon click handler
        // Checks existing permissions first, only prompts if needed
        chrome.action.onClicked.addListener((tab) => {
            if (!tab?.id || !tab?.url) return;
            const origin = originFromUrl(tab.url);
            if (!origin) return;

            console.log('app.js: Extension icon clicked for origin:', origin);

            // Check if permission already exists (synchronous check, preserves user gesture)
            chrome.permissions.contains({ origins: [`${origin}/*`] })
                .then(hasPermission => {
                    if (hasPermission) {
                        console.log('app.js: Permission already granted for:', origin);
                        // Already have permission - register if needed and inject
                        return ensureRegisteredForOrigin(origin)
                            .then(() => isContentPresent(tab.id))
                            .then(isPresent => {
                                if (!isPresent) {
                                    console.log('app.js: Injecting content script');
                                    return injectOnce(tab.id);
                                } else {
                                    console.log('app.js: Content script already present');
                                }
                            });
                    } else {
                        // No permission yet - request it (MUST be in same promise chain to preserve user gesture)
                        console.log('app.js: Requesting permission for:', `${origin}/*`);
                        return chrome.permissions.request({ origins: [`${origin}/*`] })
                            .then(granted => {
                                console.log('app.js: Permission request result:', granted);
                                if (granted) {
                                    console.log('app.js: User granted permission, registering and injecting');
                                    // Register for future auto-injection
                                    return ensureRegisteredForOrigin(origin)
                                        .then(() => isContentPresent(tab.id))
                                        .then(isPresent => {
                                            if (!isPresent) {
                                                return injectOnce(tab.id);
                                            }
                                        });
                                } else {
                                    console.log('app.js: User declined permission, injecting once with activeTab');
                                    // User declined persistent permission, but we can still inject once with activeTab
                                    return isContentPresent(tab.id).then(isPresent => {
                                        if (!isPresent) {
                                            return injectOnce(tab.id);
                                        }
                                    });
                                }
                            });
                    }
                })
                .catch(error => {
                    console.error('app.js: Error in click handler:', error);
                    // Fallback: try to inject with activeTab
                    isContentPresent(tab.id).then(isPresent => {
                        if (!isPresent) {
                            console.log('app.js: Attempting fallback injection with activeTab');
                            return injectOnce(tab.id);
                        }
                    }).catch(err => {
                        console.error('app.js: Fallback injection also failed:', err);
                    });
                });
        });

        // Keep registrations in sync when permissions change:
        chrome.permissions.onAdded.addListener(async (perm) => {
            const origins = perm?.origins || [];
            for (const pattern of origins) {
                // pattern looks like "https://example.com/*"
                const origin = pattern.slice(0, pattern.length - 1); // strip trailing '*'
                await ensureRegisteredForOrigin(origin.slice(0, -1)); // also remove trailing '/'
            }
        });

        chrome.permissions.onRemoved.addListener(async (perm) => {
            const origins = perm?.origins || [];
            for (const pattern of origins) {
                const origin = pattern.slice(0, pattern.length - 1);
                await unregisterForOrigin(origin.slice(0, -1));
            }
        });

        chrome.runtime.onInstalled.addListener(async () => {
            // Optional: migration/cleanup if you change CS ids or structure between versions.
        });
    }
}

/**
 * Called after Blazor is ready to receive calls from JS.
 * @param {any} blazor The Blazor instance
 */
export function afterStarted(blazor) {
    console.log("app.js: afterStarted");

}

