/// <reference types="chrome" />

// TODO P2 Import Polyfill for the side effect of defining a global 'browser' object vs chrome.
// import * as _ from "/content/Blazor.BrowserExtension/lib/browser-polyfill.min.js";

// TODO P2 Prior to release, cache management needs to be more explicit in order to avoid
// using cache from prior releases, etc. See advice in
// https://www.oreilly.com/library/view/building-progressive-web/9781491961643/ch04.html

import MessageSender = chrome.runtime.MessageSender;
import { Utils } from "../es6/uiHelper.js";
import { CsSwMsgType, IExCsMsgHello, SwCsMsgType } from "../es6/ExCsInterfaces.js";
import { ICsSwMsg } from "../es6/ExCsInterfaces.js";
import { connect, getSignedHeaders } from "./signify_ts_shim.js";

export const ENUMS = {
    InactivityAlarm: "inactivityAlarm"
} as const;

// Note the handlers are triggered in order: // runtime.onInstalled, this.activating, this.activated, and then others
// For details, see https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/API/runtime#events
//
// Listen for and handle a new install or update of the extension
chrome.runtime.onInstalled.addListener(async (installDetails: chrome.runtime.InstalledDetails) => {
    console.log("SW InstalledDetails: ", installDetails);
    let urlString = "";
    switch (installDetails.reason) {
        case "install":
            // TODO P2 Update Onboarding UI
            urlString = `${location.origin}/index.html?environment=tab&reason=${installDetails.reason}`;
            Utils.createTab(urlString);
            break;
        case "update":
            // This event could also be triggered from user hitting Reload on the browser's Extensions page
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

// Listen for and handle the activation event, such as to clean up old caches
self.addEventListener('activate', (event) => {
    console.log('SW activated');
});

// Listen for and handle event when the browser is launched
chrome.runtime.onStartup.addListener(() => {
    console.log('SW runtime.onStartup');
    // This handler, for when a new browser profile with the extension installed is first launched,
    // could potentially be used to set the extension's icon to a "locked" state, for example
});

// Handle messages from app (other than via port)
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message.action === 'resetInactivityTimer') {
        // Clear existing alarm and set a new one
        chrome.alarms.clear(ENUMS.InactivityAlarm, () => {
            // TODO P1 get InactivityTimout from stored preferences (cached into storage.session).  Confirm this is debounced and has no performance hit with frequently geting the delay preference
            chrome.alarms.create(ENUMS.InactivityAlarm, { delayInMinutes: 5.0 });
        });
    }
});

// When inactivityAlarm fires, remove the stored passcode
chrome.alarms.onAlarm.addListener((alarm) => {
    if (alarm.name === ENUMS.InactivityAlarm) {
        // Inactivity timeout expired
        chrome.storage.session.remove('passcode', () => {
            // Send a message to the SPA(s?) to lock the app
            try {
                chrome.runtime.sendMessage({ action: 'lockApp' }, (response) => {
                    if (chrome.runtime.lastError) {
                        console.log("SW lockApp message send to SPA failed:", chrome.runtime.lastError.message);
                    } else {
                        console.log("SW lockApp message send to SPA response: ", response);
                    }
                });
            } catch {
                console.error("SW could not sendMessage for lockApp");
            }
        });
    }
});


// Listen for and handle the browser action being clicked
chrome.action.onClicked.addListener((tab: chrome.tabs.Tab) => {
    // Note since the extension is a browser action, it needs to be able to access the current tab's URL, but with activeTab permission and not tabs permission
    // In our design, the default_action should not be defined in manifest.json, since we want to handle the click event in the service worker

    console.log("SW clicked on action button while on tab: ", tab);

    // If the tab is a web page, check if the extension has permission to access the tab (based on its origin)
    if (tab.id && Number(tab.id) != 0 && tab.url !== undefined && tab.url.startsWith("http")) {
        const tabId = Number(tab.id);

        // Check if the extension has permission to access the tab (based on its origin), and if not, request it
        const origin = new URL(tab.url!).origin + '/';
        console.log('SW origin: ', origin);
        chrome.permissions.contains({ origins: [origin] }, (isOriginPermitted: boolean) => {
            console.log('SW isOriginPermitted: ', isOriginPermitted);
            if (!isOriginPermitted) {
                // Request permission from the user
                chrome.permissions.request({
                    origins: [origin]
                }, (isGranted: boolean) => {
                    if (isGranted) {
                        console.log('SW Permission granted for:', origin);
                        useActionPopup(tabId);
                    } else {
                        console.log('SW Permission denied for:', origin);
                        Utils.createTab(`${location.origin}/index.html?environment=tab`);
                    }
                });
            } else {
                // if user clicks on the action icon on a page already allowed permission, but for an interaction not initiated from the content script
                Utils.createTab(`${location.origin}/index.html?environment=tab`);
                // useActionPopup(tabId);
            }
        });
        // Clear the popup url for the action button, if it is set, so that future use of the action button will also trigger this same handler
        chrome.action.setPopup({ popup: "", tabId: tab.id });
        return;
    }
    // If the tab is not a web page, open the extension's popup window
    else {
        Utils.createTab(`${location.origin}/index.html?environment=tab`);
        // createPopupWindow();
        return;
    }
});

let popupWindow: chrome.windows.Window | null = null;

// If a popup window is already open, bring it into focus; otherwise, create a new one
// TODO P3 each popupWindow should be associated with a tab?  Add a tabId parameter?
function usePopupWindow() {
    console.log("SW usePopupWindow");
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

// Create a new popup window (versus an action popup)
// TODO P3 each popupWindow should be associated with a tab?  Add a tabId parameter?
function createPopupWindow() {
    console.log("SW createPopupWindow");
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
            console.log("SW new extension popup window created");
            newWindow.alwaysOnTop = true;
            newWindow.state = "normal";
            popupWindow = newWindow;
        }
    });
}

// Use the action popup to interact with the user for the current tab (if it is a web page)
function useActionPopup(tabId: number, queryParams: { key: string, value: string }[] = []) {
    console.log('SW useActionPopup acting on current tab');
    queryParams.push({ key: "environment", value: "ActionPopup" });
    const url = createUrlWithEncodedQueryStrings("./index.html", queryParams)
    chrome.action.setPopup({ popup: url, tabId: tabId });
    chrome.action.openPopup()
        .then(() => console.log("SW useActionPopup succeeded"))
        .catch((err) => {
            // TODO P2 this error from openPopup() seems to throw even when the popup is opened successfully, perhaps due to a timing issue.  Ignoring for now.
            // console.warn(`SW useActionPopup dropped. Was already open?`, err);
        });
    // Clear the popup url for the action button, if it is set, so that future use of the action button will also trigger this same handler
    chrome.action.setPopup({ popup: "", tabId: tabId });
}

// Bring the window (e.g. an extension popupWindow) to focus.  requires windows permission in the manifest ?
function focusWindow(windowId: number): void {
    chrome.windows.update(windowId, { focused: true });
}

// Check if the window (e.g. an extension popupWindow) is open, e.g. to avoid opening multiple windows
async function isWindowOpen(windowId: number): Promise<boolean> {
    return new Promise((resolve) => {
        chrome.tabs.query({ windowId }, (tabs) => {
            resolve(tabs.length > 0);
        });
    });
}

function serializeAndEncode(obj: any): string {
    // TODO P2 assumes the payload obj is simple
    const jsonString: string = JSON.stringify(obj);
    const encodedString: string = encodeURIComponent(jsonString);
    return encodedString;
}

function handleSignRequest(payload: any, csTabPort: chrome.runtime.Port) {
    // ICsSwMsgSignRequest
    console.log("SW handleSignRequest: ", payload);

    // TODO P3 should check if a popup is already open, and if so, bring it into focus.
    // chrome.action.setBadgeBackgroundColor({ color: '#037DD6' });
    if (csTabPort.sender && csTabPort.sender.tab && csTabPort.sender.tab.id) {
        const tabId = Number(csTabPort.sender.tab.id);
        //chrome.action.setBadgeText({ text: "3", tabId: tabId });
        //chrome.action.setBadgeTextColor({ color: '#FF0000', tabId: tabId });
        // TODO P3 Could alternately implement the payload passing via messaging versus the URL
        // TODO P3 should start a timer so the webpage doesn't need to wait forever for a response from the user? Then return an error.

        // TODO P2 add msgRequestId?
        const jsonOrigin = JSON.stringify(csTabPort.sender.origin);
        console.log("SW handleSignRequest: tabId: ", tabId, "payload value: ", payload, "origin: ", jsonOrigin);
        const encodedMsg = serializeAndEncode(payload);

        try {
            useActionPopup(tabId, [
                { key: "message", value: encodedMsg },
                { key: "origin", value: jsonOrigin },
                { key: "popupType", value: "SignRequest" }]);
        }
        catch (error) {
            console.error("SW handleSignRequest: error invoking useActionPopup: ", error);
        }
    }
}

// Handle the web page's (Cs's) request for user to select an identifier
// TODO P2 define type for msg
function handleSelectAuthorize(msg: any /* ICsSwMsgSelectIdentifier*/, csTabPort: chrome.runtime.Port) {
    // TODO P3 Implement the logic for handling the msg
    console.log("SW handleSelectIdentifier: ", msg);
    // TODO P3 should check if a popup is already open, and if so, bring it into focus.
    // chrome.action.setBadgeBackgroundColor({ color: '#037DD6' });
    if (csTabPort.sender && csTabPort.sender.tab && csTabPort.sender.tab.id) {
        const tabId = Number(csTabPort.sender.tab.id);
        //chrome.action.setBadgeText({ text: "3", tabId: tabId });
        //chrome.action.setBadgeTextColor({ color: '#FF0000', tabId: tabId });
        // TODO P1 Could alternately implement the msg passing via messaging versus the URL
        // TODO P3 should start a timer so the webpage doesn't need to wait forever for a response from the user? Then return an error.

        // TODO P2 add msgRequestId?
        const jsonOrigin = JSON.stringify(csTabPort.sender.origin);
        console.log("SW handleSelectIdentifier: tabId: ", tabId, "message value: ", msg, "origin: ", jsonOrigin);

        const encodedMsg = serializeAndEncode(msg);

        useActionPopup(tabId, [{ key: "message", value: encodedMsg }, { key: "origin", value: jsonOrigin }, { key: "popupType", value: "SelectAuthorize" }]);
    } else {
        console.warn("SW handleSelectIdentifier: no tabId found")
    }
};

// Check if the actionPopup is already open
function isActionPopupUrlSet(): Promise<boolean> {
    return new Promise((resolve, reject) => {
        chrome.action.getPopup({}, (popupUrl) => {
            if (chrome.runtime.lastError) {
                console.info("SW isActionPopupOpen: popupUrl: ", popupUrl);
                reject(chrome.runtime.lastError);
            } else {
                resolve(!!popupUrl);
            }
        });
    });
}

// Object to track the pageCsConnections between the service worker and the content scripts, using the tabId as the key
interface CsConnection {
    port: chrome.runtime.Port;
    tabId: number;
    pageAuthority: string;
}
let pageCsConnections: { [key: string]: CsConnection } = {};

// Listen for and handle port pageCsConnections from content script and Blazor App
chrome.runtime.onConnect.addListener(async (connectedPort: chrome.runtime.Port) => {
    console.log("SW onConnect port: ", connectedPort);
    let connectionId = connectedPort.name;
    console.log(`SW ${Object.keys(pageCsConnections).length} connections before update: `, pageCsConnections);

    // Get the tabId from the port, which will be a number if a browser tab from the contentScript, -1 if an action popup App, or undefined if not a tab
    let tabId = -1;
    if (connectedPort.sender?.tab?.id) {
        tabId = connectedPort.sender?.tab?.id;
    }

    console.log("SW tabId: ", tabId);
    // store the port for this tab in the pageCsConnections object. Assume 1:1
    pageCsConnections[connectionId] = { port: connectedPort, tabId: tabId, pageAuthority: "?" };
    console.log(`SW ${Object.keys(pageCsConnections).length} connections after update: `, pageCsConnections);

    // First check if the port is from a content script and its pattern
    const cSPortNamePattern = /^[0-9a-f]{8}-[0-9a-f]{8}-[0-9a-f]{8}-[0-9a-f]{8}$/;
    if (cSPortNamePattern.test(connectedPort.name)) {
        const cSPort: chrome.runtime.Port = connectedPort;
        // TODO P2 test and update assumptions of having a longrunning port established, especially when sending.  With back-forward cache (bfcache), ports can get suspended or terminated, leading to errors such as:
        // "Unchecked runtime.lastError: The page keeping the extension port is moved into back/forward cache, so the msg channel is closed."
        console.log(`SW with CS via port`, cSPort);

        // Listen for and handle messages from the content script and Blazor app
        cSPort.onMessage.addListener((message: ICsSwMsg) => handleMessageFromPageCs(message, cSPort, tabId, connectionId));

    } else {
        // Check if the port is from the Blazor App, based on naming pattern
        if (connectedPort.name.substring(0, 8) === "blazorAppPort".substring(0, 8)) {
            // TODO P3 react to port names that are more descriptive and less likely to conflict if multiple Apps are open
            // On the first csConnection, associate this port from the Blazor App with the port from the same tabId?

            const appPort = connectedPort;
            console.log(`SW with App via port`, appPort);
            // Get get the authority from the tab's origin
            let url = "unknown"
            if (appPort.sender?.url) {
                url = appPort.sender.url;
            }
            // console.log(`SW from App: url:`, url);
            const authority = getAuthorityFromOrigin(url);  // TODO P3 why not use origin string directly?
            console.log(`SW from App: authority:`, authority);

            // Update the pageCsConnections list with the URL's authority, so the SW-CS and SW-App pageCsConnections can be associated
            pageCsConnections[appPort.name].pageAuthority = authority || "unknown";
            // console.log(`SW from App: pageCsConnections:`, pageCsConnections);

            // Find the matching csConnection based on the page authority. TODO P3 should this also be based on the tabId?
            const cSConnection = findMatchingConnection(pageCsConnections, appPort.name)
            console.log(`SW from App connection:`, pageCsConnections[appPort.name], `ContentScriptConnection`, cSConnection);

            // Add a listener for messages from the App, where the handler can process and forward to the content script as appropriate.
            console.log("SW adding onMessage listener for App port, csConnection, tabId, connectionId", appPort, cSConnection, tabId, connectionId);
            appPort.onMessage.addListener(async (message) => await handleMessageFromApp(message, appPort, cSConnection, tabId, connectionId));
            console.log("SW adding onMessage listener for App port... done", appPort);

            // Send an initial msg from SW to App
            appPort.postMessage({ type: SwCsMsgType.FSW, data: 'Service worker connected' });

        } else {
            console.error('Invalid port:', connectedPort);
        }
    }

    // Clean up when the port is disconnected.  See also chrome.tabs.onRemoved.addListener
    connectedPort.onDisconnect.addListener(() => {
        console.log("SW port closed connection for page connection: ", pageCsConnections[connectionId])

        if (pageCsConnections[connectionId].port?.name.substring(0,17) == "blazorAppPort-tab") {
            // The extension's App disconnected when its window closed, which might have been in a Tab, Popup, or Action Popup.
            console.info('SW KERI Auth Extension Popup closed');
            for (var key in pageCsConnections) {
                if (pageCsConnections.hasOwnProperty(key)) {
                    const csConnection : CsConnection = pageCsConnections[key];
                    if (csConnection.tabId != -1) {
                        const lastGasp = {
                            type: SwCsMsgType.REPLY,
                            error: { code: 501, message: "User closed KERI Auth or canceled pending request" },
                            payload: {},
                        };
                        try {
                            csConnection.port.postMessage(lastGasp);
                        } catch {
                            console.log("SW could not send lastGasp to closed page connection");
                        }
                    }
                }
            }
        } else {
            // The Tab was closed.
            console.info('SW: Content Script tab closed or navigated away');
        }
        delete pageCsConnections[connectionId];
    });
});

// Listen for and handle when a tab is closed. Remove related csConnection from list.
chrome.tabs.onRemoved.addListener((tabId) => {
    if (pageCsConnections[tabId]) {
        console.log("SW tabs.onRemoved: tabId: ", tabId)
        delete pageCsConnections[tabId]
    };
});

async function handleMessageFromApp(message: any, appPort: chrome.runtime.Port, cSConnection: { port: chrome.runtime.Port, tabId: Number, pageAuthority: string } | undefined, tabId: number, connectionId: string): Promise<void> {

    console.log(`SW from App message, port:`, message, appPort);
    // TODO P3 check for nonexistance of appPort.sender?.tab, which would indicate a msg from a non-tab source

    // Send a response to the KeriAuth App
    // TODO P2 this seems like active feedback?
    appPort.postMessage({ type: SwCsMsgType.FSW, data: `SW received your message: ${message.data} for tab ${appPort.sender?.tab}` });

    // Forward the msg to the content script, if appropriate
    if (cSConnection) {
        console.log("SW from App: handling App message of type: ", message.type);
        // note the following may expose a passcode
        // console.log("SW from App: handling App message data: ", message.data);
        switch (message.type) {
            case SwCsMsgType.REPLY:
                cSConnection.port.postMessage(message);
                break;
            case "ApprovedSignRequest":
                try {
                    // TODO P0 EE! don't hardcode agentUrl and passcode, but pass these in as an argument for now.
                    const jsonSignifyClient = await connect("https://keria-dev.rootsid.cloud/admin", "Ap31Xt-FGcNXpkxmBYMQn");
                    const payload = message.payload;
                    const initHeaders: { [key: string]: string } = { method: payload.requestMethod, path: payload.requestUrl };
                    const headers: { [key: string]: string } = await getSignedHeaders(payload.origin, payload.requestUrl, payload.requestMethod, initHeaders, payload.selectedName);
                    console.log("SW: signedRequest: ", headers);

                    const signedHeaderResult = {
                        type: SwCsMsgType.REPLY,
                        requestId: message.requestId,
                        payload: { headers },
                        rurl: payload.requestUrl
                    };
                    console.log("SW from App: signedHeaderResult", signedHeaderResult);
                    cSConnection.port.postMessage(signedHeaderResult);
                    break;
                }
                catch(error) {
                    console.error("Sw from App: service-worker: ApprovedSignRequest: ", error);
                }
                break;
            case "/KeriAuth/signify/replyCredential":
                try {
                    const credObject = JSON.parse(message.payload.credential.rawJson);
                    const expiry = Math.floor((new Date().getTime() + 30 * 60 * 1000) / 1000);
                    const authorizeResultCredential = { credential: { raw: credObject, cesr: message.payload.credential.cesr }, expiry: expiry };
                    const authorizeResult = {
                        type: SwCsMsgType.REPLY,
                        requestId: message.requestId,
                        payload: authorizeResultCredential,
                        rurl: ""  // TODO P2 rurl should not be fixed
                    };
                    console.log("SW from App: authorizeResult", authorizeResult);
                    cSConnection.port.postMessage(authorizeResult);
                }
                catch (error) {
                    console.error("SW from App: error parsing credential: ", error);
                }
                break;
            case "/KeriAuth/signify/replyCancel":
                try {
                    const cancelResult = {
                        type: SwCsMsgType.REPLY,
                        requestId: message.requestId,
                        payload: {},
                        rurl: ""
                    };
                    console.log("SW from App: authorizeResult", cancelResult);
                    cSConnection.port.postMessage(cancelResult);
                }
                catch (error) {
                    console.error("SW from App: error parsing credential: ", error);
                }
                break;
            default:
                console.info("SW from App: message type not yet handled: ", message);
        }
    }
};

function handleMessageFromPageCs(message: ICsSwMsg, cSPort: chrome.runtime.Port, tabId: number, connectionId: string) {
    console.log("SW from CS: message", message);

    // assure tab is still connected  
    if (pageCsConnections[connectionId]) {
        switch (message.type) {
            case CsSwMsgType.SELECT_AUTHORIZE:
            case CsSwMsgType.SELECT_AUTHORIZE_AID:
            case CsSwMsgType.SELECT_AUTHORIZE_CREDENTIAL:
                handleSelectAuthorize(message as any, pageCsConnections[connectionId].port);
                break;
            case CsSwMsgType.SIGN_DATA:
            // TODO P2 request user to sign data (or request?)
            case CsSwMsgType.SIGN_REQUEST:
                handleSignRequest(message as any, pageCsConnections[connectionId].port);
                break;
            case CsSwMsgType.SIGNIFY_EXTENSION:
                pageCsConnections[connectionId].tabId = tabId;
                const url = new URL(String(cSPort.sender?.url));
                pageCsConnections[connectionId].pageAuthority = url.host;
                const response: IExCsMsgHello = {
                    type: SwCsMsgType.HELLO,
                    requestId: message.requestId,
                    payload: {}
                };
                cSPort.postMessage(response);
                break;
            default:
                console.warn("SW from CS: message type not yet handled: ", message);
        }
    } else {
        console.log("SW Port no longer connected");
    }
}

// Create a URL with the provided base URL and query parameters, verifying that the keys are well-formed
function createUrlWithEncodedQueryStrings(baseUrl: string, queryParams: { key: string, value: string }[]): string {
    const url = new URL(chrome.runtime.getURL(baseUrl));
    const params = new URLSearchParams();
    queryParams.forEach(param => {
        if (isValidKey(param.key)) {
            params.append(encodeURIComponent(param.key), encodeURIComponent(param.value));
        } else {
            console.warn(`Invalid key skipped: ${param.key}`);
        }
    });
    url.search = params.toString();
    return url.toString();
}

// Check if the provided key is well-formed
function isValidKey(key: string): boolean {
    // A simple regex to check for valid characters in a key
    // Adjust the regex based on what you consider "well-formed"
    const keyRegex = /^[a-zA-Z0-9-_]+$/;
    return keyRegex.test(key);
}

// Based on the provided url and key, extract key's decoded value from the query string
function getQueryParameter(url: string, key: string): string | null {
    const parsedUrl = new URL(url);
    const params = new URLSearchParams(parsedUrl.search);
    const encodedValue = params.get(key);
    if (encodedValue) {
        return decodeURIComponent(decodeURIComponent(encodedValue));
    }
    return null;
}

// Extract the authority portion (hostname and port) from the provided URL's query string, origin key
function getAuthorityFromOrigin(url: string): string | null {
    const origin = getQueryParameter(url, 'origin');
    const unquotedOrigin = origin?.replace(/^["'](.*)["']$/, '$1');
    if (origin) {
        try {
            const originUrl = new URL(String(unquotedOrigin));
            return originUrl.host;
        } catch (error) {
            console.error('Invalid origin URL:', error);
        }
    }
    return null;
}

// Find the first matching csConnection based on the provided key and its page authority value
function findMatchingConnection(connections: { [key: string]: { port: chrome.runtime.Port, tabId: Number, pageAuthority: string } }, providedKey: string): { port: chrome.runtime.Port, tabId: Number, pageAuthority: string } | undefined {
    const providedConnection = connections[providedKey];
    if (!providedConnection) {
        return undefined;
    }
    const targetPageAuthority = providedConnection.pageAuthority;
    for (const key in connections) {
        if (key !== providedKey && connections[key].pageAuthority === targetPageAuthority) {
            return connections[key];
        }
    }
    return undefined;
}

export { };