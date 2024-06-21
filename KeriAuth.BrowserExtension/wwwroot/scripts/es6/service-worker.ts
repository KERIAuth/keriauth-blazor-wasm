/// <reference types="chrome" />
/* <reference types="serviceworker" />
*/
const version = "0.0.1"; // Increment this version number for every update needing cache invalidation
console.log(`WORKER: service-worker.ts version ${version}`);
// TODO Import Polyfill for the side effect of defining a global 'browser' object vs chrome.
// import * as _ from "/content/Blazor.BrowserExtension/lib/browser-polyfill.min.js";

// TODO Prior to release, cache management needs to be more explicit in order to avoid
// using cache from prior releases, etc. See advice in
// https://www.oreilly.com/library/view/building-progressive-web/9781491961643/ch04.html

import MessageSender = chrome.runtime.MessageSender;
import { Utils } from "./uiHelper.js";

//
// Common definitions for this content script and the extension service-worker.
// Note these are manually repeated here and in the ContentScript,
// because of the CommonJS module system that must be used for this ContentScript.
// A fix would be to use a separate CommonInterface.ts file and a bundler to build the content script, but that is not yet implemented.
//

// Message types from CS to SW
type CsSwMsg = ICsSwMsgSelectIdentifier | ICsSwMsgSelectCredential;
interface ICsSwMsg {
    type: string
}

enum CsSwMsgType {
    SELECT_IDENTIFIER = "select-identifier",
    SELECT_CREDENTIAL = "select-credential",
    SIGNIFY_EXTENSION = "signify-extension",
    SELECT_ID_CRED = "select-aid-or-credential",
    SELECT_AUTO_SIGNIN = "select-auto-signin",
    NONE = "none",
    VENDOR_INFO = "vendor-info",
    FETCH_RESOURCE = "fetch-resource",
    AUTO_SIGNIN_SIG = "auto-signin-sig",
    DOMCONTENTLOADED = "document-loaded"
}

interface ICsSwMsgSelectIdentifier extends ICsSwMsg {
    type: CsSwMsgType.SELECT_IDENTIFIER
}

interface ICsSwMsgHello extends ICsSwMsg {
    type: CsSwMsgType.DOMCONTENTLOADED
}

interface ICsSwMsgSelectCredential extends ICsSwMsg {
    type: CsSwMsgType.SELECT_CREDENTIAL
    data: any
}

// Message types from Extension to CS
interface IExCsMsg {
    type: string
}

enum ExCsMsgType {
    HELLO = "hello"
}

interface IExCsMsgHello extends IExCsMsg {
    type: ExCsMsgType.HELLO
}

// The following handlers trigger in order:
// runtime.onInstalled, this.activating, this.activated, and then others
// See runtime events here: https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/API/runtime#events

// Bring up the Onboarding page if a new install
chrome.runtime.onInstalled.addListener(async (installDetails) => {
    console.log(`WORKER: onInstalled details:`, installDetails);
    // Pre-cache files or perform other installation tasks
    // await RegisterContentScripts();
    let urlString = "";
    switch (installDetails.reason) {
        case "install":
            // TODO P2 Update Onboarding UI
            urlString = `${location.origin}/index.html?environment=tab&reason=${installDetails.reason}`;
            Utils.createTab(urlString);
            break;
        case "update":
            // this could also result from user hitting Reload on the Extensions page
            // TODO P3 Allow the index page to know whether the version of the cache is not the new manifest's version?
            urlString = `${location.origin}/index.html?environment=tab&reason=${installDetails.reason}&priorVersion=${encodeURIComponent(installDetails.previousVersion!)}`;
            Utils.createTab(urlString);
            break;
        case "chrome_update":
        case "shared_module_update":
        default:
            break;
    }
});

//self.addEventListener('activating', (event: ExtendableEvent) => {
//    console.log(`WORKER: version ${version} activating...`);
//    // event.waitUntil(self.clients.claim());
//    // Perform tasks needed during activation
//});

self.addEventListener('activate', (event) => {
    console.log('WORKER: activated');
    //event.waitUntil(
    //    (async () => {
    //        // Perform tasks needed after activation
    //        // For example, clean up old caches
    //        const cacheNames = await caches.keys();
    //        await Promise.all(
    //            cacheNames.map((cacheName) => {
    //                if (/* condition to determine old cache */) {
    //                    return caches.delete(cacheName);
    //                }
    //            })
    //        );
    //    })()
    //);
});

// Handle network requests and serving cached responses
// self.addEventListener('fetch', (event) => {
// way to noisy for now to echo every fetch
// console.log('WORKER: Fetch event for ', event);
// Handle network requests and serving cached responses
//. event.request.url);
//event.respondWith(
//    caches.match(event.request).then((response) => {
//        return response || fetch(event.request);
//    })
//);
// });

chrome.runtime.onStartup.addListener(() => {
    console.log('WORKER: runtime.onStartup');
    // This handler, for when a new browser profile with the extension installed is first launched, 
    // could potentially be used to set the extension's icon to a "locked" state, for example
});

chrome.action.onClicked.addListener((tab: chrome.tabs.Tab) => {
    // Note since the extension is a browser action, it needs to be able to access the current tab's URL, but with activeTab permission and not tabs permission
    // In addition, the default_action cannot be defined in manifest.json. See https://developer.chrome.com/docs/extensions/reference/action/#default_popup
    //
    // will not fire if the action has a popup
    //
    // When the user clicks this extension's action button, this starts a sequence of events. One typical sequence is below, which ends with the popup being opened.
    // 1. This handler is invoked
    // 2. TBD
    // 3. TBD

    console.log("WORKER: clicked on action button while on tab: ", tab);

    if (tab.id && Number(tab.id) != 0 && tab.url !== undefined && tab.url.startsWith("http")) {
        // TODO create a helper for creating the popupDetails(tab). DRY
        const tabId = Number(tab.id);
        // chrome.action.getPopup({ tabId: tabId }, (popupUrl) => { console.log("WORKER: getPopup: ", popupUrl) });
        // chrome.action.setPopup({ popup: "index.html?environment=ActionPopup" }) //  tabId: tabId,
        //    .then(() => {
        //chrome.action.openPopup()
        //    .then(() => {
        //console.log("WORKER: user clicked on action button");
        //if (typeof tab.url !== 'string') {
        //    chrome.action.setPopup({ popup: "", tabId: tab.id })
        //        .then(() => { return; })
        //}
        const origin = new URL(tab.url!).origin + '/';
        console.log('WORKER: origin: ', origin);
        chrome.permissions.contains({ origins: [origin] }, (isOriginPermitted: boolean) => {
            console.log('WORKER: isOriginPermitted: ', isOriginPermitted);
            if (!isOriginPermitted) {
                // Request permission from the user
                chrome.permissions.request({
                    origins: [origin]
                }, (isGranted: boolean) => {
                    if (isGranted) {
                        console.log('WORKER: Permission granted for:', origin);
                        useActionPopup(tabId);
                    } else {
                        console.log('WORKER: Permission denied for:', origin);
                    }
                });
            } else {
                useActionPopup(tabId);
            }
        });
        //   })
        //   .catch((err) => console.warn(`WORKER: could not openPopup`, { tabId: tabId, tab: tab, err: err }))
        //    })
        //    .catch((err) => console.warn(`WORKER: openPopup dropped`, err));
        // clear the popup url so that subsequent clicks on popup will be handled by this onClicked listener
        // TODO correct?
        chrome.action.setPopup({ popup: "", tabId: tab.id });
        return;
    }
    else {
        createPopupWindow();
        return;
    }
});

let popupWindow: chrome.windows.Window | null = null;

// If a popup window is already open, then bring it into focus; otherwise, create a new one
// TODO: each popupWindow should be associated with a tab?  Add a tabId parameter?
function usePopupWindow() {
    console.log("WORKER: usePopupWindow");
    if (popupWindow && popupWindow.id) {
        isWindowOpen(popupWindow.id).then((isOpen) => {
            if (isOpen && popupWindow && typeof popupWindow.id === 'number') {
                focusWindow(popupWindow.id);
                return;
            } else {
                createPopupWindow();
            }
        });
    }
    else {
        createPopupWindow();
    }
}

// TODO: each popupWindow should be associated with a tab?  Add a tabId parameter?
function createPopupWindow() {
    console.log("WORKER: createPopupWindow");
    // TODO P3 Rather than having a fixed position, it would be better to compute this left position
    // based on the windows's or device's availableWidth,
    // as well as knowing on which monitor it belongs (versus assuming the primary monitor).
    // This fix is involved, since it may require "system.display" permission in manifest, and
    // additional messaging exchange between the background and injected content script.
    // Also need to handle (and ideally avoid) potential exceptions if the window is too big 
    // for the screen or attempted to be placed off the screen.
    const popupWindowCreateData = Object.freeze({
        type: "popup",
        url: chrome.runtime.getURL("/index.html?environment=BrowserPopup"),
        height: 638,
        width: 390,
        top: 100,
        left: 100
    }) as chrome.windows.CreateData
    chrome.windows.create(popupWindowCreateData, (newWindow) => {
        if (newWindow) {
            console.log("WORKER: new extension popup window created");
            newWindow.alwaysOnTop = true;
            newWindow.state = "normal";
            popupWindow = newWindow;
        }
    });
}

function useActionPopup(tabId: number) {
    console.log('WORKER: useActionPopup acting on current tab');
    //chrome.tabs.query({ active: true, currentWindow: true }, tabs => {
    //    console.log('WORKER: useActionPopup tabs: ', tabs);
    //    let tab = tabs[0];
    //    if (typeof tab?.url === 'string') {
    //        console.log("WORKER: useActionPopup tab url: ", tab.url);
    //        if (typeof tab.id === 'number') {
    //            //chrome.action.setPopup({ popup: "./index.html?environment=ActionPopup" });
    //            //isActionPopupUrlSet()
    //            //    .then(isOpen => {
    //            //        if (!isOpen) {
    chrome.action.setPopup({ popup: "./index.html?environment=ActionPopup", tabId: tabId });
    chrome.action.openPopup()
        .then(() => console.log("WORKER: useActionPopup succeeded"))
        .catch((err) => {
            console.warn(`WORKER: useActionPopup dropped. Was already open?`, err);
            //chrome.action.setPopup({ popup: "./index.html?environment=ActionPopup", tabId: tabId });
            //chrome.action.openPopup().then(() =>
            //    console.log("WORKER: useActionPopup re-opened"))
            //    .catch((err) => console.warn("WORKER: useActionPopup re-open dropped: ", err));
        });

    //}
    //else {
    //    console.warn("WORKER: useActionPopup: unexpected tab");
    //}
    //} else {
    //    console.warn("WORKER: useActionPopup: unexpected tab or url");
    //}
    //})
}

// bring the window to focus.  requires windows permission in the manifest ?
function focusWindow(windowId: number): void {
    chrome.windows.update(windowId, { focused: true });
}

// The following was moved back into the manifest.json file
//async function RegisterContentScripts() {
//    console.log("WORKER: Registering ContentScripts...")
//    try {
//        await (chrome.scripting as any).unregisterContentScripts(async function () {
//            try {
//                await chrome.scripting.registerContentScripts([
//                    {
//                        id: 'KeriAuthContentScript',
//                        matches: ["https://*/*", "http://*/*"],
//                        "js": ["/scripts/commonjs/ContentScript.js"],
//                        "runAt": "document_start",
//                        world: "ISOLATED",
//                        allFrames: true
//                    }
//                ]);
//            } catch (err) {
//                console.warn(`WORKER: ContentScripts registration dropped: ${err}`);
//            }
//        })
//        console.log("WORKER: ContentScripts registered")
//    } catch (err) {
//        console.warn(`WORKER: ContentScripts registration dropped: ${err}`);
//    }
//};

async function isWindowOpen(windowId: number): Promise<boolean> {
    return new Promise((resolve) => {
        chrome.tabs.query({ windowId }, (tabs) => {
            resolve(tabs.length > 0);
        });
    });
}

function handleSelectIdentifier(msg: ICsSwMsgSelectIdentifier, port: chrome.runtime.Port) {
    // TODO P3 Implement the logic for handling the message
    console.log("WORKER: handleSelectIdentifier: ", msg);
    // TODO EE should check if a popup is already open, and if so, bring it into focus.
    // chrome.action.setBadgeBackgroundColor({ color: '#037DD6' });
    if (port.sender && port.sender.tab && port.sender.tab.id) {
        const tabId = Number(port.sender.tab.id);
        chrome.action.setBadgeText({ text: "3", tabId: tabId });
        chrome.action.setBadgeTextColor({ color: '#FF0000', tabId: tabId });
        useActionPopup(tabId);
    } else {
        console.warn("WORKER: handleSelectIdentifier: no tabId found")
    }
};

// Function to check if the actionPopup is already open
function isActionPopupUrlSet(): Promise<boolean> {
    return new Promise((resolve, reject) => {
        chrome.action.getPopup({}, (popupUrl) => {
            console.warn("WORKER: isActionPopupOpen: popupUrl: ", popupUrl);
            if (chrome.runtime.lastError) {
                reject(chrome.runtime.lastError);
            } else {
                resolve(!!popupUrl);
            }
        });
    });
}

// Object to store connection info
const connections: { [key: string]: { port: chrome.runtime.Port } } = {};

// Listen for port connections from content scripts
chrome.runtime.onConnect.addListener(async (connectedPort: chrome.runtime.Port) => {
    console.log("WORKER: onConnect port: ", connectedPort);
    let connectionId = connectedPort.name;
    console.log("WORKER: connections before update: ", { connections });
    // store the port for this tab in the connections object. Assume 1:1
    connections[connectionId] = { port: connectedPort };
    console.log("WORKER: connections: ", { connections });

    const portNamePattern = /^[0-9a-f]{8}-[0-9a-f]{8}-[0-9a-f]{8}-[0-9a-f]{8}$/;
    if (portNamePattern.test(connectedPort.name)) {
        console.log(`WORKER: Connected to ${connectedPort.name}`);

        // Listen for and handle messages from the content script
        console.log("WORKER: Adding onMessage listener for port");
        connectedPort.onMessage.addListener((message: any) => {
            console.log("WORKER: from CS: message, port", message, connectedPort);
            // assure tab is still connected        
            if (connections[connectionId]) {
                switch (message.type) {
                    case CsSwMsgType.SELECT_IDENTIFIER:
                        handleSelectIdentifier(message as ICsSwMsgSelectIdentifier, connections[connectionId].port);
                        break;
                    case CsSwMsgType.SIGNIFY_EXTENSION:
                        const response: IExCsMsgHello = {
                            type: ExCsMsgType.HELLO
                        };
                        connectedPort.postMessage(response);
                        break;
                    case CsSwMsgType.AUTO_SIGNIN_SIG:
                    case CsSwMsgType.FETCH_RESOURCE:
                    case CsSwMsgType.VENDOR_INFO:
                    case CsSwMsgType.NONE:
                    case CsSwMsgType.SELECT_AUTO_SIGNIN:
                    case CsSwMsgType.SELECT_CREDENTIAL:
                    case CsSwMsgType.SELECT_ID_CRED:
                    case CsSwMsgType.DOMCONTENTLOADED:
                    default:
                        console.warn("WORKER: from CS: message type not yet handled: ", message);
                }
            } else {
                console.log("WORKER: Port no longer connected");
            }
        });
        // Clean up when the port is disconnected.  See also chrome.tabs.onRemoved.addListener
        connectedPort.onDisconnect.addListener(() => {
            delete connections[connectionId];
        });

    } else {
        console.error('Invalid port name:', connectedPort.name);
    }
});

// Listen for tab updates to maintain connection info
// TODO can probably remove this tabs.onUpdated listener
chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
    // console.log("WORKER: tabs.onUpdated: tabId: ", tabId, " changeInfo: ", changeInfo, " tab: ", tab)
    //
    // if the url changes to another domain, then the connection should be closed?
    //if (connections[tabId] && changeInfo.url) {
    //    connections[tabId].url = changeInfo.url;
    //}
});

// Remove connection from list when a tab is closed
chrome.tabs.onRemoved.addListener((tabId) => {
    if (connections[tabId]) {
        console.log("WORKER: tabs.onRemoved: tabId: ", tabId)
        delete connections[tabId]
    };
});

export { };