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
        // You can perform background-specific initialization here if needed

        // Note this onClicked handler is here to preserve user gesture for permission requests, which must be done in direct response to user action, and the user gesture is lost if we call into C# first.
        // Note that the default_action is intentionally not defined in our manifest.json, since the click event handling will be dependant on UX state and permissions, and we want minimal permissions declared in the manifest, to minimize scary worded warnings from the browser during the extension installation
        chrome.action.onClicked.addListener(async (tab) => {
            if (!tab?.id || !tab.url) return;

            // 1) Compute per-origin match patterns from the clicked tab
            const MATCHES = matchPatternsFromTabUrl(tab.url);
            if (MATCHES.length === 0) {
                console.log("app.js: Unsupported or restricted URL scheme; not registering persistence.", tab.url);
                return;
            }

            // 2) Request persistent host permission FIRST (while user gesture is still active)
            // Note: request() must be the first async call to preserve the user gesture
            const wanted = { origins: MATCHES };
            const granted = await chrome.permissions.request(wanted);
            if (!granted) {
                console.log("app.js: User declined persistent host permission; stopping.");
                return;
            }

            // 3) Check if persistent content script is already registered for this origin
            const scriptId = `${CS_ID_PREFIX}-${new URL(tab.url).hostname}`;
            const already = await chrome.scripting.getRegisteredContentScripts({ ids: [scriptId] });

            if (already.length > 0) {
                console.log("KERIAuth BW: %cPersistent content script already registered:%c", 'font-weight:bold; color:red', scriptId, "- skipping one-shot injection");
                return;
            } else {
                console.log("app.js: proceeding with one-shot injection and then persistent registration");


                // 4) One-shot injection NOW (authorized via activeTab + user click)
                /*
                try {
                    const results = await chrome.scripting.executeScript({
                        target: { tabId: tab.id, allFrames: true },
                        world: "MAIN", // or "ISOLATED" (safer default)
                        func: injectedOnce,
                        args: ["Hello from first run!"]
                    });
                    console.log("KERIAuth BW: One-shot results per frame:", results);
                } catch (e) {
                    console.error("KERIAuth BW: One-shot injection failed:", e);
                    return;
                }
                */


                // 5) Register a persistent content script for this origin
                await chrome.scripting.registerContentScripts([{
                    id: scriptId,
                    js: ["scripts/esbuild/ContentScript.js"],
                    matches: MATCHES,          // derived from the tab's origin
                    runAt: "document_idle",    // or "document_start"/"document_end"
                    allFrames: true,
                    world: "ISOLATED"
                    // TODO P2: should the following be true or false?
                    // persistAcrossSessions: true, // default is true
                }]);
                console.log("app.js: Registered persistent content script:", scriptId, "for", MATCHES);
                
            };
        });
    }
}


/**
 * Build match patterns from the tab's URL, suitable for both:
 * - chrome.permissions.{contains,request}({ origins: [...] })
 * - chrome.scripting.registerContentScripts({ matches: [...] })
 *
 * Notes:
 * - Match patterns don't include ports; they'll match any port on that host.
 * - Only http/https/file are supported for injection.
 * - file:// access still requires the user to enable "Allow access to file URLs"
 *   on the extension's details page, even if permission is granted here.
 */
function matchPatternsFromTabUrl(tabUrl) {
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

/**
 * 
 *Function serialized into the page for the one-shot run 
 */
function injectedOnce(message) {
    console.log("app.js: One-shot injected in", window.location.href, "message:", message);
    return { href: location.href, title: document.title };
}

/**
 * Called after Blazor is ready to receive calls from JS.
 * @param {any} blazor The Blazor instance
 */
export function afterStarted(blazor) {
    console.log("app.js: afterStarted");

}

