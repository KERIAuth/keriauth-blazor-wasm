// Blocktrust Identity Wallet Extension
/// <reference types="chrome" />

// TODO P4 Import Polyfill for the side effect of defining a global 'browser' variable
// import * as _ from "/content/Blazor.BrowserExtension/lib/browser-polyfill.min.js";

// TODO P3 Prior to release, cache management needs to be more explicit in order to avoid
// using cache from prior releases, etc. See advice in
// https://www.oreilly.com/library/view/building-progressive-web/9781491961643/ch04.html

import MessageSender = chrome.runtime.MessageSender;
// import { IMessage  } from '../commonjs/CommonInterfaces.js';

// TODO should be referenced from the CommonInterfaces.ts file
export interface IMessage {
    name: string,
    sourceHostname: string;
    sourceOrigin: string;
    windowId: number;
}

import { Utils } from "./uiHelper.js";

// The following handlers trigger in order:
// runtime.onInstalled, this.activating, this.activated, and then others
// See runtime events here: https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/API/runtime#events

// Bring up the Onboarding page if a new install
chrome.runtime.onInstalled.addListener(async (installDetails) => {
    console.log(`WORKER: onInstalled handler: reason=${installDetails.reason}`);
    await RegisterContentScripts();
    let urlString = "";
    switch (installDetails.reason) {
        case "install":
            // TODO P2 Update Onboarding UI
            urlString = `${location.origin}/index.html?environment=tab&reason=${installDetails.reason}`;
            Utils.createTab(urlString);
            break;
        case "update":
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
    // 2. A script is run in the context of the current page, which sends a message to this background script with the page's URL
    // 3. This background script checks if it already has permission to the page origin, and asks the user for such permission if not
    // 4. This background script injects the ContentScripts into the current page
    // 5. This background script opens the popup

    console.log("WORKER: clicked on action button while on tab: ", tab);

    // reset the popup to the nothing state, so that the popup is not opened (unless set below) when the action button is clicked
    
    chrome.action.setPopup({ popup: "" });

    if (tab.id !== undefined && tab.url !== undefined && tab.url.startsWith("http")) {
        /*
        chrome.action.setPopup({ popup: "./index.html?environment=ActionPopup" })
            .then(() => chrome.action.openPopup()  // TODO only available to policy installed extensions??
                .then(() => console.log("WORKER: opened popup"))
                .catch((err) => console.warn(`WORKER: could not openPopup`))
            )
            .catch((err) => console.warn(`WORKER: openPopup dropped: ${err}`));
        chrome.action.setPopup({ popup: "" });
        return;
        */
    }
    else {
        createPopupWindow();
        return;
    }
    
});

function popup(tabId2: number) {
    console.log("WORKER: popup: tabId: ", String(tabId2));
    try {
        chrome.scripting.executeScript({
            target: { tabId: tabId2 },
            func: getTabUrlAndContinue,
            args: [tabId2]
        }, (injectionResult) => {
            if (chrome.runtime.lastError) {
                console.warn(`WORKER: onClicked: executeScript result: ${chrome.runtime.lastError.message}`);
                // bring up the popup window anyway
                console.warn(`WORKER: onClicked: executeScript result: ${injectionResult}`);
                // usePopup();
                chrome.action.setPopup({ popup: "./index.html&envirnoment=ActionPopup" });
                chrome.action.openPopup()
                    .then(() => console.log("WORKER: openPopup succeeded"))
                    .catch((err) => console.warn(`WORKER: openPopup dropped: ${err}`));
                return;
            } else {
                console.warn(`WORKER: onClicked: executeScript result: ${injectionResult}`);
            }
        });
    } catch (err) {
        console.warn(`WORKER: onClicked: executeScript dropped: ${err}`);
    }
}

let popupWindow: chrome.windows.Window | null = null;

// If a popup window is already open, then bring it into focus; otherwise, create a new one
function usePopup() {
    console.log("WORKER: usePopup");
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

function getTabUrlAndContinue(tabId: number): chrome.scripting.InjectionResult<string> {
    try {
        // Note this will be executed in the page's context, not the extension context
        let responseMessage: IMessage = {
            name: "getTabUrlResponse",
            sourceHostname: window.location.href,
            sourceOrigin: "",
            windowId: tabId // not really a windowId, but a tabId
        };
        // Send the message with URL back to the extension service-worker
        chrome.runtime.sendMessage(responseMessage);
        return {
            result: String(window.location.href),
            documentId: "",
            frameId: 0,
        };
    } catch (err) {
        console.warn(`WORKER: getTabUrlAndContinue dropped: ${err}`);
        return {
            result: "failure",
            documentId: "",
            frameId: 0,
        }
    }
}


chrome.runtime.onMessage.addListener((message: IMessage, sender: MessageSender, sendResponse: (response: string) => void) => {
    console.log('WORKER: runtime.onMessage: ', message, " sender: ", sender);

    if (message.name === 'getTabId') {
        console.log('WORKER: from CS: getTabId: ', sender.tab?.id);
        // send the tabId back to the content script.
        // TODO EE! should be in a proper message type
        sendResponse(String(sender.tab?.id));
        return true;
    }

    // Check for conformance of message to IMessage, and ignore if not
    if (message == undefined
        || typeof message.name !== 'string'
        // || typeof message.walletRequest !== 'object'
        // || typeof message.sourceHostname !== 'string'
        // || typeof message.sourceOrigin !== 'string'
        // || typeof message.contentHash !== 'string'
        // || typeof message.timestamp !== 'number'
        // || typeof message.windowId !== 'number'
        // || typeof message.iframeMode !== 'boolean'
    ) {
        console.log('WORKER: onMessage handler ignoring nonconformant message: ', message);
    }
    else {
        let messageOrigin: string | undefined = sender.origin;

        // tmp
        console.log('WORKER: onMessage: ', message, ' from: ', messageOrigin);


        switch (message.name) {
            case "getTabUrlResponse":
                // Handle message from the script running in context of the page.
                // With the page's URL, request permission if needed, so that permission is granted and remembered for future requests during the browser session

                if (typeof message.sourceHostname !== 'string') {
                    console.log('WORKER: getTabUrlResponse: ignoring because of unexpected sourceHostname: ', message.sourceHostname);
                }
                else {
                    const origin = new URL(message.sourceHostname).origin + '/';
                    console.log('WORKER: getTabUrlResponse: origin: ', origin);

                    // Check if we already have permission to the page origin
                    chrome.permissions.contains({ origins: [origin] }, (isOriginPermitted: boolean) => {
                        console.log('WORKER: getTabUrlResponse: isOriginPermitted: ', isOriginPermitted);
                        if (!isOriginPermitted) {
                            // Request permission from the user
                            chrome.permissions.request({
                                origins: [origin]
                            }, (isGranted: boolean) => {
                                if (isGranted) {
                                    console.log('WORKER: Permission granted for:', origin);
                                    reloadTabAndUsePopup();
                                } else {
                                    console.log('WORKER: Permission denied for:', origin);
                                    // TODO if there is already an open tab of this name, reuse it.
                                    chrome.tabs.create({ url: "./index.html?environment=tab" });
                                }
                            });
                        } else {
                            reloadTabAndUsePopup();
                        }
                    });
                }
                //    case "WALLET_REQUEST_MSG":
                //        if (!(message.walletRequest && message.sourceHostname && message.sourceOrigin && message.contentHash)) {
                //            return "invalid message"
                //        }
                //        if (message.sourceOrigin != messageOrigin) {
                //            return "invalid origin";
                //        }

                //        // Adding the current windowId
                //        if (sender.tab !== undefined) {
                //            message.windowId = sender.tab.windowId;
                //        } else {
                //            message.windowId = 0;
                //        }

                //        // Bring up notifcation in a new window
                //        // be aware of async handling insinde the processing of messages https://stackoverflow.com/questions/53024819/chrome-extension-sendresponse-not-waiting-for-async-function/
                //        GetWalletRequests().then(r => SetWalletRequest(r, message)).then(
                //            async () => {
                //                if (message.walletRequest && !message.iframeMode) {
                //                    usePopup();
                //                }
                //            }
                //        );
                break;
            default:
                break;
        }
        return true; // Keeps the message channel open until `sendResponse` is invoked
    }
});

// TODO reload of tab should not be required
function reloadTabAndUsePopup() {
    console.log('WORKER: reloadTabAndPopup acting on current tab');
    chrome.tabs.query({ active: true, currentWindow: true }, tabs => {
        console.log('WORKER: reloadTabAndPopup tabs: ', tabs);
        let tab = tabs[0];
        if (typeof tab?.url === 'string') {
            console.log("WORKER: reloadTabAndPopup tab url: ", tab.url);
            if (typeof tab.id === 'number') {
                // TODO P2 Below is a hack to get the active page to reload, this time with the injected content scripts. It should be done in a more elegant way.
                chrome.tabs.reload(tab.id).then(() => {
                    chrome.action.setPopup({ popup: "./index.html?environment=ActionPopup" });
                    chrome.action.openPopup()
                        .then(() => console.log("WORKER: openPopup 1111 succeeded"))
                        .catch((err) => console.warn(`WORKER: openPopup 1111 dropped: ${err}`));
                    // usePopup();
                });
            }
        }
    })
}

// bring the window to focus.  requires windows permission in the manifest ?
function focusWindow(windowId: number): void {
    chrome.windows.update(windowId, { focused: true });
}

async function RegisterContentScripts() {
    console.log("WORKER: Registering ContentScripts...")
    try {
        await (chrome.scripting as any).unregisterContentScripts(async function () {
            try {
                await chrome.scripting.registerContentScripts([
                    {
                        id: 'KeriAuthContentScript',
                        matches: ["https://*/*", "http://*/*"],
                        "js": ["/scripts/commonjs/ContentScript.js"],
                        "runAt": "document_start",
                        world: "ISOLATED",
                        allFrames: true
                    }
                ]);
            } catch (err) {
                console.warn(`WORKER: ContentScripts registration dropped: ${err}`);
            }
        })
        console.log("WORKER: ContentScripts registered")
    } catch (err) {
        console.warn(`WORKER: ContentScripts registration dropped: ${err}`);
    }
};

async function isWindowOpen(windowId: number): Promise<boolean> {
    return new Promise((resolve) => {
        chrome.tabs.query({ windowId }, (tabs) => {
            resolve(tabs.length > 0);
        });
    });
}

function handleSelectIdentifier(msg: IMessage, port: chrome.runtime.Port) {
    // TODO P3 Implement the logic for handling the message
    console.log("WORKER: handleSelectIdentifier: ", msg);
    // TODO EE should check if a popup is already open, and if so, bring it into focus.
    // chrome.action.setBadgeBackgroundColor({ color: '#037DD6' });
    chrome.action.setBadgeText({ text: "3", tabId: Number(port.name) });
    chrome.action.setBadgeTextColor({ color: '#FF0000', tabId: Number(port.name) });
    // popup(msg.windowId);
};

// TODO P3 experiment with a counter of waiting actions, network/node status, and/or Locked state
// Could also put a lock icon, see: https://developer.chrome.com/docs/extensions/reference/action/#method-setIcon
// BadgeText should be different per tab, since extension actions can have different states for each tab. if tabId is omitted, the setting is treated as a global.
// See https://developer.chrome.com/docs/extensions/reference/action/#per-tab-state
// });

//chrome.runtime.onConnect.addListener((port: chrome.runtime.Port) => {
//    console.assert(port.name === "connect-to-service");

//    port.onMessage.addListener((request: any) => {
//        console.log("WORKER: from CS:");
//        console.log(request);
//        if (request.greeting === "hello") {
//            console.log("WORKER: from CS:", String(request.greeting));
//            port.postMessage({ farewell: "goodbye" });
//        } 
//    });

//    port.onDisconnect.addListener(() => {
//        console.log("WORKER: Port disconnected");
//    });
//});


// Object to store connection info
const connections: { [key: number]: { port: chrome.runtime.Port, url?: string } } = {};

const CsExMsgType = Object.freeze({
    SELECT_IDENTIFIER: "select-identifier",
    SELECT_CREDENTIAL: "select-credential",
    SELECT_ID_CRED: "select-aid-or-credential",
    SELECT_AUTO_SIGNIN: "select-auto-signin",
    NONE: "none",
    VENDOR_INFO: "vendor-info",
    FETCH_RESOURCE: "fetch-resource",
    AUTO_SIGNIN_SIG: "auto-signin-sig",
})


// Listen for port connections from content scripts
chrome.runtime.onConnect.addListener((port: chrome.runtime.Port) => {
    // Store the connection info
    // TODO verify that the port name is a number
    var tabId = Number(port.name);
    // store the port in the connections object
    connections[tabId] = { port: port };
    console.log("WORKER: connections: ", connections);

    // Listen for and handle messages from the content script
    port.onMessage.addListener((request: IMessage) => {
        console.log("WORKER: from CS:", request);
        // assure tab is still connected        
        if (connections[tabId]) {
            switch (request.name) {
                case CsExMsgType.SELECT_IDENTIFIER:
                    handleSelectIdentifier(request, connections[tabId].port);
                    break;
                case "signify-extension":
                    break;
                case CsExMsgType.AUTO_SIGNIN_SIG:
                case CsExMsgType.FETCH_RESOURCE:
                case CsExMsgType.VENDOR_INFO:
                case CsExMsgType.NONE:
                case CsExMsgType.SELECT_AUTO_SIGNIN:
                case CsExMsgType.SELECT_CREDENTIAL:
                case CsExMsgType.SELECT_ID_CRED:
                default:
                    console.warn("WORKER: request not yet implemented");
            }
        } else {
            console.log("WORKER: Port no longer connected");
        }
    });

    // Clean up when the port is disconnected
    port.onDisconnect.addListener(() => {
        delete connections[tabId];
    });
});



// Listen for tab updates to maintain connection info
chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
    // console.log("WORKER: tabs.onUpdated: tabId: ", tabId, " changeInfo: ", changeInfo, " tab: ", tab)
    //
    // TODO if the url changes to another domain, then the connection should be closed
    //if (connections[tabId] && changeInfo.url) {
    //    connections[tabId].url = changeInfo.url;
    //}
});

// Clean up when a tab is closed
chrome.tabs.onRemoved.addListener((tabId) => {
    delete connections[tabId];
});

export { };