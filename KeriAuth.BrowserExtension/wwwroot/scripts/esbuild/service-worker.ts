/// <reference types="chrome" />

import { Utils } from "../es6/uiHelper.js";
import { CsSwMsgEnum, ICsSwMsg, ISwCsMsgPong, SwCsMsgEnum } from "../es6/ExCsInterfaces.js";
import { connect, getSignedHeaders, getNameByPrefix, getIdentifierByPrefix } from "./signify_ts_shim.js";
import { UpdateDetails } from "../types/types.js";

export const ENUMS = {
    InactivityAlarm: "inactivityAlarm"
} as const;

// Note the runtime handlers are triggered in order: // runtime.onInstalled, this.activating, this.activated, and then others
// For details, see https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/API/runtime#events

// Listen for and handle a new install or update of the extension
chrome.runtime.onInstalled.addListener(async (installDetails: chrome.runtime.InstalledDetails) => {
    console.log("SW InstalledDetails: ", installDetails);
    let urlString = "";
    switch (installDetails.reason) {
        case "install":
            urlString = `${location.origin}/index.html?environment=tab&reason=${installDetails.reason}`;
            Utils.createTab(urlString);
            break;
        case "update":
            // This will be triggered by a Chrome Web Store push,
            // or, when sideloading in development, by installing an updated release per the manifest or a Refresh in DevTools.
            const previousVersion = installDetails.previousVersion;
            const currentVersion = chrome.runtime.getManifest().version;
            console.log(`Extension updated from version ${previousVersion} to ${currentVersion}.`);
            // Save update information for later use on user's next normal activity
            const updateDetails: UpdateDetails = {
                reason: installDetails.reason,
                previousVersion: previousVersion,
                currentVersion: currentVersion,
                timestamp: new Date().toISOString(),
            };
            await chrome.storage.local.set({ UpdateDetails: updateDetails });
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
        // Default inactivity timeout
        let inactivityTimeoutMinutes = 5.0;

        // Get the user setting from session storage
        chrome.storage.session.get(['inactivityTimeoutMinutes'], (result) => {
            if (result.inactivityTimeoutMinutes !== undefined) {
                const timeout = parseFloat(result.inactivityTimeoutMinutes);
                if (!isNaN(timeout)) {
                    inactivityTimeoutMinutes = timeout;
                }
            }

            // Clear existing alarm and set a new one
            chrome.alarms.clear(ENUMS.InactivityAlarm, () => {
                chrome.alarms.create(ENUMS.InactivityAlarm, { delayInMinutes: inactivityTimeoutMinutes });
            });
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
    // Note since the extension is a browser action, it needs to be able to access the current tab's URL,
    // but with activeTab permission and not tabs permission.
    // In our design, the default_action is not defined in manifest.json, 
    // since we want to handle the click event in the service worker depending on UX state.

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
    else {
        // The tab is not a usual tab here?
        Utils.createTab(`${location.origin}/index.html?environment=tab`);
        return;
    }
});

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

function serializeAndEncode(obj: any): string {
    // TODO P2 assumes the payload obj is simple
    const jsonString: string = JSON.stringify(obj);
    const encodedString: string = encodeURIComponent(jsonString);
    return encodedString;
}


async function getIdentifierNameForCurrentTab(origin: string): Promise<string> {
    // check if there is a passcode in session storage, which indicates app is unlocked
    const result = await chrome.storage.session.get(['passcode']);
    if (result.passcode) {
        // TODO P2 fix this assumption that uses the first/only connection. Fix needed when multiple tabs are supported
        // console.log("SW handleSignRequest pageCsConnections:", pageCsConnections);
        const cSConnection = pageCsConnections[Object.keys(pageCsConnections)[0]];

        // add to message with additional properties of origin and selectedName. Could alternately add these to message if updated everywhere needed

        const websiteConfig = await getWebsiteConfigByOrigin(origin);
        // console.warn("SW handleSignRequest websiteConfig:", websiteConfig);
        // TODO P2 add better error handling above and if name below is null

        const result = await chrome.storage.local.get(['KeriaConnectConfig']);
        if (result.KeriaConnectConfig && result.KeriaConnectConfig.AdminUrl) {
            const adminUrl = result.KeriaConnectConfig.AdminUrl as string;
            const passcodeRes = await chrome.storage.session.get(['passcode']);
            if (passcodeRes.passcode) {
                const jsonSignifyClient = await connect(adminUrl, passcodeRes.passcode);
                // console.warn("SW handleSignRequest jsonSignifyClient:", jsonSignifyClient);
                const name = await getNameByPrefix(websiteConfig.rememberedPrefixOrNothing);
                return name;
            }
        }
    }
}

// TODO P2 type of any is a code smell
async function handleSignRequest(message: any, csTabPort: chrome.runtime.Port) {
    // ICsSwMsgSignRequest
    console.log("SW handleSignRequest: ", message);

    if (csTabPort.sender?.tab?.id) {
        const tabId = Number(csTabPort.sender.tab.id);
        // TODO P3 Could alternately implement the message passing via messaging versus the URL

        const jsonOrigin = JSON.stringify(csTabPort.sender.origin);
        console.log("SW handleSignRequest: tabId: ", tabId, "payload value: ", message, "origin: ", jsonOrigin);
        const encodedMsg = serializeAndEncode(message);

        if (message.payload?.method) {
            switch (message.payload.method) {
                case "GET":
                case "HEAD":
                case "OPTIONS":
                    try {
                        // If tab is requesting a safe request, then don't launch an action popup.
                        // TODO P2 clean up website config preferences about "use safe headers"
                        // We are ignoring the "use safe headers flag" here and just doing it.

                        // This approach assumes we know the selected identifier and credential from website config, created during a prior sign-in
                        const identifierName = await getIdentifierNameForCurrentTab(csTabPort.sender.origin);
                        if (identifierName) {
                            (message as any).payload.selectedName = identifierName;
                            console.log("SW handleSignRequest message:", message);
                            await signReqSendToTab(message, csTabPort);
                            return;
                        }
                        // intentionally falling through to default case now
                    } catch (error) {
                        console.error("handleSignRequest error on method ", message.payload.method, " error:", error);
                    }
                default:
                    try {
                        useActionPopup(tabId, [
                            { key: "message", value: encodedMsg },
                            { key: "origin", value: jsonOrigin },
                            { key: "popupType", value: "SignRequest" }]);
                        return;
                    }
                    catch (error) {
                        console.error("SW handleSignRequest: error invoking useActionPopup: ", error);
                    };
            }
        }
    }
}

function getWebsiteConfigByOrigin(origin: string): Promise<any | undefined> {
    return new Promise((resolve, reject) => {
        chrome.storage.local.get("WebsiteConfigList", (result) => {
            if (chrome.runtime.lastError) {
                reject(chrome.runtime.lastError);
                return;
            }

            // Check if the result contains the Websites array
            const websites = result?.WebsiteConfigList?.Websites;

            if (Array.isArray(websites)) {
                // Find the object matching the provided origin
                const matchingWebsite = websites.find(
                    (website) => website.origin === origin
                );
                resolve(matchingWebsite);
            } else {
                // Resolve with undefined if Websites array is not found
                resolve(undefined);
            }
        });
    });
}


// Handle the tab content script's request for user to select an identifier
// TODO P2 define type for msg. Use of any is a code smell.
function handleSelectAuthorize(msg: any /* ICsSwMsgSelectIdentifier*/, csTabPort: chrome.runtime.Port) {
    // TODO P3 Implement the logic for handling the msg
    console.log("SW handleSelectAuthorize: ", msg);
    // TODO P3 should check if a popup is already open, and if so, bring it into focus.
    // chrome.action.setBadgeBackgroundColor({ color: '#037DD6' });
    if (csTabPort.sender && csTabPort.sender.tab && csTabPort.sender.tab.id) {
        const tabId = Number(csTabPort.sender.tab.id);
        //chrome.action.setBadgeText({ text: "3", tabId: tabId });
        //chrome.action.setBadgeTextColor({ color: '#FF0000', tabId: tabId });
        // TODO P2 Could alternately implement the msg passing via messaging versus the URL
        // TODO P3 should start a timer so the webpage doesn't need to wait forever for a response from the user? Then return an error.
        const jsonOrigin = JSON.stringify(csTabPort.sender.origin);
        console.log("SW handleSelectAuthorize: tabId: ", tabId, "message value: ", msg, "origin: ", jsonOrigin);
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
    // TODO P2 check assumption above?
    pageCsConnections[connectionId] = { port: connectedPort, tabId: tabId, pageAuthority: "?" };
    console.log(`SW ${Object.keys(pageCsConnections).length} connections after update: `, pageCsConnections);

    // First check if the port is from a content script and its pattern
    const cSPortNamePattern = /^[0-9a-f]{8}-[0-9a-f]{8}-[0-9a-f]{8}-[0-9a-f]{8}$/;
    if (cSPortNamePattern.test(connectedPort.name)) {
        const cSPort: chrome.runtime.Port | null = connectedPort;
        // TODO P2 test and update assumptions of having a longrunning port established, especially when sending.  With back-forward cache (bfcache), ports can get suspended or terminated, leading to errors such as:
        // "Unchecked runtime.lastError: The page keeping the extension port is moved into back/forward cache, so the msg channel is closed."
        console.log(`SW with CS via port`, cSPort);

        // Listen for and handle messages from the content script and Blazor app
        cSPort.onMessage.addListener(async (message: ICsSwMsg) => await handleMessageFromPageCs(message, cSPort, tabId, connectionId));

    } else {
        // Check if the port is from the Blazor App, based on naming pattern
        if (connectedPort.name.substring(0, 8) === "blazorAppPort".substring(0, 8)) {
            // TODO P3 react to port names that are more descriptive and less likely to conflict if multiple Apps are open
            // On the first csConnection, associate this port from the Blazor App with the port from the same tabId?

            // TODO P2 There could potentially be many appPorts?, if multiple tabs were open in same browser profile.
            // The interactions are currently (2025-01-03) not being isolated between each tab
            const appPort = connectedPort;
            console.log(`SW with App via port`, appPort); // just the Action Popup?
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

            // Add a listener for messages from the App, 
            // where the handler can process and forward to the tab's content script as appropriate.
            console.log("SW adding onMessage listener for App port, csConnection, tabId, connectionId", appPort, cSConnection, tabId, connectionId);
            appPort.onMessage.addListener(async (message) => await handleMessageFromApp(message, appPort, cSConnection, tabId, connectionId));
            console.log("SW adding onMessage listener for App port... done", appPort);

            // Send an initial msg from SW to App
            appPort.postMessage({ type: SwCsMsgEnum.FSW, data: 'Service worker connected' });

        } else {
            console.error('Invalid port:', connectedPort);
        }
    }

    // Clean up when the port is disconnected.  See also chrome.tabs.onRemoved.addListener
    connectedPort.onDisconnect.addListener(() => {
        console.log("SW port closed connection for page connection: ", pageCsConnections[connectionId])

        if (pageCsConnections[connectionId].port?.name.substring(0, 17) == "blazorAppPort-tab") {
            // The extension's App disconnected when its window closed, which might have been in a Tab, Popup, or Action Popup.
            console.info('SW KERI Auth Extension Popup closed');
            for (var key in pageCsConnections) {
                if (pageCsConnections.hasOwnProperty(key)) {
                    const csConnection: CsConnection = pageCsConnections[key];
                    if (csConnection.tabId != -1) {
                        const lastGasp = {
                            type: SwCsMsgEnum.REPLY,
                            error: "User closed KERI Auth or canceled pending request",
                            requestId: 999999,  // TODO P3: maybe this requestId value is filled in by content script?
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

// Connect so that shim will work as expected
// TODO P2 should only have one "signify-service" like connection? Perhaps add support for multiple tabs with potentially
// different selected identifiers first, before concluding this design.
async function connectToKeria(): Promise<boolean> {
    try {
        const result = await chrome.storage.local.get(['KeriaConnectConfig']);
        if (result.KeriaConnectConfig && result.KeriaConnectConfig.AdminUrl) {
            const adminUrl = result.KeriaConnectConfig.AdminUrl as string;
            const result2 = await chrome.storage.session.get(['passcode']);
            if (result2.passcode) {
                const jsonSignifyClient = await connect(adminUrl, result2.passcode);
                return true;
            }
        }
        else {
            console.error("could not connect to KERIA with config and passcode.");
            return false;
        }
    } catch (error) {
        console.error("Could not connect to KERIA: ", error)
        return false;
    }
}

// TODO P2 type of any is a code smell
async function signReqSendToTab(message: any, port: chrome.runtime.Port) {
    // Assure connection to KERIA, then sign Http Request Headers, then send to CS
    try {
        if (await connectToKeria()) {
            const payload = message.payload;
            const initHeaders: { [key: string]: string } = { method: payload.method, path: payload.url };
            // console.warn("tmp signReqSendToTab message: ", message);
            const headers: { [key: string]: string } = await getSignedHeaders(payload.origin, payload.url, payload.method, initHeaders, payload.selectedName);
            const signedHeaderResult = {
                type: SwCsMsgEnum.REPLY,
                requestId: message.requestId,
                payload: { headers },
                rurl: payload.requestUrl
            };
            console.log("SW signReqSendToTab: signedHeaderResult", signedHeaderResult);
            port.postMessage(signedHeaderResult);
        }
        else {
            throw new Error("SW signReqSendToTab: unexpected KERIA non-availability or passcode issue");
        }
    }
    catch (error) {
        console.error("SW signReqSendToTab:", error);
    }
}

async function handleMessageFromApp(message: any, appPort: chrome.runtime.Port, cSConnection: { port: chrome.runtime.Port, tabId: Number, pageAuthority: string } | undefined, tabId: number, connectionId: string): Promise<void> {

    console.log(`SW from App message, port:`, message, appPort);

    // Send a response to the KeriAuth App
    // TODO P3 is this unecessary noise, or helpful for debugging?
    appPort.postMessage({ type: SwCsMsgEnum.FSW, data: `SW received your message: ${message.data} for tab ${appPort.sender?.tab}` });

    // Forward the msg to the content script, if appropriate
    if (cSConnection) {
        console.log("SW from App: handling App message of type: ", message.type);
        switch (message.type) {
            // TODO P2 update and define consistent message type enums here
            case SwCsMsgEnum.REPLY:
                cSConnection.port.postMessage(message);
                break;
            case "ApprovedSignRequest":
                await signReqSendToTab(message, cSConnection.port);
                break;
            case "/KeriAuth/signify/replyCredential":
                try {
                    // TODO P2 Improve typing.  E.g., const msg = message as TypeFoo<DataBar>
                    const credObject = JSON.parse(message.payload.credential.rawJson);
                    const expiry = Math.floor((new Date().getTime() + 30 * 60 * 1000) / 1000);

                    // TODO P2 Why fake urls here and below for rurl?
                    const isConnected = await connectToKeria();
                    if (isConnected) {
                        const headers = await getSignedHeaders("example.com", "https://example.com", "GET", credObject, message.payload.identifier.alias);
                        // TODO P2 constrain to a type
                        const authorizeResultCredential =
                        {
                            credential:
                            {
                                raw: credObject,
                                cesr: message.payload.credential.cesr
                            },
                            expiry: expiry,
                            headers: headers
                        };
                        // TODO P2 constrain to a type
                        const authorizeResult = {
                            type: SwCsMsgEnum.REPLY,
                            requestId: message.requestId,
                            payload: authorizeResultCredential,
                            rurl: ""
                        };
                        console.log("SW from App: authorizeResult", authorizeResult);
                        cSConnection.port.postMessage(authorizeResult);
                    } else {
                        throw Error("could not connect");
                        // TODO P2 do a graceful replyCancel instead
                    }
                }
                catch (error) {
                    console.error("SW: error processing ", message.type as string, ": ", error);
                }
                break;
            case "/KeriAuth/signify/replyCancel":
                try {
                    // TODO P2 constrain to a type
                    const cancelResult = {
                        type: SwCsMsgEnum.REPLY,
                        requestId: message.requestId,
                        payload: {},
                        rurl: ""
                    };
                    console.log("SW from App: authorizeResult", cancelResult);
                    cSConnection.port.postMessage(cancelResult);
                }
                catch (error) {
                    console.error("SW: error processing ", message.type as string, ": ", error);
                }
                break;
            default:
                console.info("SW from App: message type not yet handled: ", message);
        }
    } else {
        console.info("SW cSConnection was closed, so cannot send to CS");
    }
};

async function handleMessageFromPageCs(message: ICsSwMsg, cSPort: chrome.runtime.Port, tabId: number, connectionId: string) {
    console.log("SW from CS: message", message);

    // assure tab is still connected  
    if (pageCsConnections[connectionId]) {
        switch (message.type) {
            case CsSwMsgEnum.POLARIS_SIGNIFY_AUTHORIZE:
            case CsSwMsgEnum.POLARIS_SELECT_AUTHORIZE_AID:
            case CsSwMsgEnum.POLARIS_SELECT_AUTHORIZE_CREDENTIAL:
                handleSelectAuthorize(message as any, pageCsConnections[connectionId].port);
                break;
            case CsSwMsgEnum.POLARIS_SIGN_REQUEST:
                // TODO P2 any is a code smell
                await handleSignRequest(message as any, pageCsConnections[connectionId].port);
                break;
            case CsSwMsgEnum.INIT:
                pageCsConnections[connectionId].tabId = tabId;
                const url = new URL(String(cSPort.sender?.url));
                pageCsConnections[connectionId].pageAuthority = url.host;
                const response: ISwCsMsgPong = {
                    type: SwCsMsgEnum.READY,
                    requestId: message.requestId,
                    payload: {}
                };
                console.log("SW to CS: ", SwCsMsgEnum.READY)
                cSPort.postMessage(response);
                break;
            case CsSwMsgEnum.POLARIS_SIGN_DATA:
                // TODO P1 request user to sign data (or request?)
            default:
                console.warn("SW from CS: message type not yet handled: ", message);
        }
    } else {
        console.log("SW Port no longer connected");
    }
}

// Create a URL with the provided base URL and query parameters, verifying that the keys are well-formed
// TODO P3 would base64 encoding be better?
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
// TODO P3 would base64 encoding be better?
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