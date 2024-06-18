﻿// Blocktrust Identity Wallet Extension
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

interface ICsSwMsg extends IMessage {
    name2: string,
}

interface IExCsMsg extends IMessage {
    name3: string,
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






    if (tab.id !== undefined && tab.url !== undefined && tab.url.startsWith("http")) {
        chrome.action.setPopup({ popup: "./index.html?environment=ActionPopup", tabId: tab.id })
            .then(() => {
                chrome.action.openPopup()
                    .then(() => {
                        console.log("WORKER: user clicked on action button");
                        if (typeof tab.url !== 'string') {
                            chrome.action.setPopup({ popup: "", tabId: tab.id })
                                .then(() => { return; })
                        }
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
                                        // useActionPopup();
                                    } else {
                                        console.log('WORKER: Permission denied for:', origin);
                                        // TODO if there is already an open tab of this name, reuse it.
                                        // chrome.tabs.create({ url: "./index.html?environment=tab" });
                                    }
                                });
                            } else {
                                // useActionPopup();
                            }
                        });
                        // in any case, send the tabId back to the content script.
                        // send the tabId back to the content script.
                        // TODO EE! should be in a proper message type with type "tabId"?
                        // sendResponse(String(sender.tab?.id));
                    })
                    .catch((err) => console.warn(`WORKER: could not openPopup`, tab, err))
            })
            .catch((err) => console.warn(`WORKER: openPopup dropped: ${err}`));
        // clear the popup url so that subsequent clicks on popup will be handled by this onClicked listener
        chrome.action.setPopup({ popup: "", tabId: tab.id });
        return;
    }
    else {
        createPopupWindow();
        return;
    }
});

// Unused?
function popup(tabId2: number) {
    console.log("WORKER: popup: tabId: ", String(tabId2));
    chrome.scripting.executeScript({
        target: { tabId: tabId2 },
        func: getTabUrlAndContinue,
        args: [tabId2]
    }, (injectionResult) => {
        if (injectionResult === undefined) {
            if (chrome.runtime.lastError) {
                console.warn(`WORKER: onClicked: executeScript result:`, chrome.runtime.lastError.message);
            } else {
                console.warn(`WORKER: onClicked: executeScript result: failed for unknown reasons`);
            }
        } else {
            console.log(`WORKER: onClicked: executeScript result:`, injectionResult);
            chrome.action.setPopup({ popup: "./index.html?envirnoment=ActionPopup", tabId: tabId2 }); // todo pass some reason, e.g. to prompt for AID. reset the popup URL later!
            chrome.action.openPopup()
                .then(() => console.log("WORKER: openPopup succeeded"))
                .catch((err) => console.warn(`WORKER: openPopup dropped: ${err}`));
        }
    });
}

let popupWindow: chrome.windows.Window | null = null;

// If a popup window is already open, then bring it into focus; otherwise, create a new one
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

// obsolete?
function getTabUrlAndContinue(tabId: number): chrome.scripting.InjectionResult<string> {
    try {
        //console.log("getTabUrlAndContinue: getContexts...");
        //chrome.runtime.getContexts({}, ((a) => {
        //    console.log("getTabUrlAndContinue: ", a);
        //}));

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

// listen to non-port messages from the content script
//chrome.runtime.onMessage.addListener((message: IMessage, sender: MessageSender, sendResponse: (response: string) => void) => {
//    console.log('WORKER: runtime.onMessage: ', message, " sender: ", sender);

//    if (sender.tab && sender.tab.id && message.name === 'getTabId') {
//        console.log(`WORKER: from CS: getTabId: Sender's tabId:`, sender.tab.id);
//        return sender.tab.id.toString();
//    }
//    else {
//        console.error('WORKER: runtime.onMessage: unexpected message: ', message);
//        return "ignored unexpected message";
//    }
//});

function useActionPopup() {
    console.log('WORKER: useActionPopup acting on current tab');
    chrome.tabs.query({ active: true, currentWindow: true }, tabs => {
        console.log('WORKER: useActionPopup tabs: ', tabs);
        let tab = tabs[0];
        if (typeof tab?.url === 'string') {
            console.log("WORKER: useActionPopup tab url: ", tab.url);
            if (typeof tab.id === 'number') {
                //chrome.action.setPopup({ popup: "./index.html?environment=ActionPopup" });
                //isActionPopupUrlSet()
                //    .then(isOpen => {
                //        if (!isOpen) {
                chrome.action.openPopup()
                    .then(() => console.log("WORKER: useActionPopup succeeded"))
                    .catch((err) => {
                        console.warn(`WORKER: useActionPopup dropped. Was already open?: ${err}`);
                        //chrome.action.setPopup({ popup: "./index.html?environment=ActionPopup", tabId: tabId });
                        //chrome.action.openPopup().then(() =>
                        //    console.log("WORKER: useActionPopup re-opened"))
                        //    .catch((err) => console.warn("WORKER: useActionPopup re-open dropped: ", err));
                    });
                //    } else {
                //        console.log('WORKER: useActionPopup: Popup is already open.');
                //    }
                //})
                //.catch(error => {
                //    console.error('WORKER: useActionPopup: Error checking popup status:', error);
                //});
            }
            else {
                console.warn("WORKER: useActionPopup: unexpected tab");
            }
        } else {
            console.warn("WORKER: useActionPopup: unexpected tab or url");
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
    useActionPopup();
    // popup(Number(port.name));
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
    const portNamePattern = /^[0-9a-f]{8}-[0-9a-f]{8}-[0-9a-f]{8}-[0-9a-f]{8}$/;
    // store the port in the connections object
    connections[tabId] = { port: port };
    console.log("WORKER: connections: ", connections);

    if (portNamePattern.test(port.name)) {
        console.log(`WORKER: Connected to ${port.name}`);

        port.onMessage.addListener((message: IMessage) => {
            console.log('WORKER: Received message from content script:', message);








            console.log("WORKER: from CS:", message);
            // assure tab is still connected        
            if (connections[tabId]) {
                switch (message.name) {
                    case "Hello from content script!":
                        // Respond to the content script
                        var response: IExCsMsg = { name: 'Hello from service worker', name3: 'hell O', sourceHostname: message.sourceHostname, sourceOrigin: message.sourceOrigin, windowId: message.windowId };
                        port.postMessage(response);
                        break;
                    case CsExMsgType.SELECT_IDENTIFIER:
                        handleSelectIdentifier(message, connections[tabId].port);
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
        // Clean up when the port is disconnected.  See also chrome.tabs.onRemoved.addListener
        port.onDisconnect.addListener(() => {
            delete connections[tabId];
        });

    } else {
        console.error('Invalid port name:', port.name);
    }
});

// Listen for tab updates to maintain connection info
chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
    console.log("WORKER: tabs.onUpdated: tabId: ", tabId, " changeInfo: ", changeInfo, " tab: ", tab)
    //
    // TODO if the url changes to another domain, then the connection should be closed?
    //if (connections[tabId] && changeInfo.url) {
    //    connections[tabId].url = changeInfo.url;
    //}
});

// Clean up when a tab is closed
chrome.tabs.onRemoved.addListener((tabId) => {
    console.log("WORKER: tabs.onRemoved: tabId: ", tabId)
    delete connections[tabId];
});

export { };