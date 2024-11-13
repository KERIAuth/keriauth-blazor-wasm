/// <reference types="chrome" />

// This ContentScript is inserted into pages after a user provided permission for the site (after having clicked on the extension action button)
// The purpose of the ContentScript is primarily to shuttle messages from a web page to/from the extension service-worker.

const currentOrigin = window.location.origin;

// windows message event data
interface EventData {
    type: string;
    [key: string]: any;
}

import { CsSwMsgType, IExCsMsgHello, SwCsMsgType, ISwCsMsg, ICsSwMsg, CsToPageMsgIndicator, KeriAuthMessageData, ISignin, ICredential, } from "../es6/ExCsInterfaces.js";
import {
    AuthorizeResultCredential,
    AuthorizeArgs,
    AuthorizeResultIdentifier,
    AuthorizeResult,
    SignDataArgs,
    SignDataResultItem,
    SignDataResult,
    SignRequestArgs,
    SignRequestResult,
    ConfigureVendorArgs,
    MessageData
} from "../es6/PageCsInterfaces.js"

// Function to generate a unique and unguessable identifier for the port name for communications between the content script and the extension
function generateUniqueIdentifier(): string {
    const array = new Uint32Array(4);
    window.crypto.getRandomValues(array); // TODO P4 consider randumUUID() instead
    return Array.from(array, dec => ('00000000' + dec.toString(16)).slice(-8)).join('-');
}

// Create the unique port name for communications between the content script and the extension
const uniquePortName: string = generateUniqueIdentifier();

function advertiseToPage(): void {
    // TODO P2 the following should be typed and ideally imported from PageCsInterfaces
    const msg = {
        type: SwCsMsgType.SE,
        data: { extensionId: String(chrome.runtime.id) },
        source: CsToPageMsgIndicator
    }
    postMessageToPage<unknown>(msg);
}

function postMessageToPage<T>(msg2: T): void {
    console.log("KeriAuthCs to page:", msg2);
    window.postMessage(msg2, currentOrigin);
}

// Handle messages from the extension
function handleMessageFromServiceWorker(message: MessageData<unknown>, port: chrome.runtime.Port): void {
    // TODO P3 move this into its own function for readability
    console.log("KeriAuthCs from SW:", message);

    if (message.type) {
        // console.error("KeriAuthCs from SW: message type found:", message);
        // return;
    } else {
        console.error("KeriAuthCs from SW: type not found in message:", message);
        return;
    }

    switch (message.type) {
        case SwCsMsgType.HELLO:
            window.addEventListener("message", (event: MessageEvent<EventData>) => handleWindowMessage(event, port));
            break;
        case SwCsMsgType.REPLY:
            const msg: KeriAuthMessageData<AuthorizeResult> = {
                type: message.type,
                requestId: message.requestId,
                payload: message.payload,
                error: message.error,
                source: CsToPageMsgIndicator,
                rurl: "" // TODO P2 rurl should not be fixed
            }
            postMessageToPage<KeriAuthMessageData<AuthorizeResult>>(msg);
            break;
        case SwCsMsgType.SE:
            console.log("intentionally ignoring type signify-extension here, as it is handled in advertiseToPage function.")
            break;
        case SwCsMsgType.CANCELED:
            // last gasp
            /*
            const msg2: any = {
                type: "/signify/reply",
                error: { code: 404, message: "User canceled request or closed KERI Auth"},
                source: CsToPageMsgIndicator,
                requestId: lastRequestIdFromPage,
                // payload: {},
                // rurl: ""
            }
            postMessageToPage<unknown>(msg2);
            */
            break;
        default:
            console.error("KeriAuthCs from SW: handler not implemented for message type:", message.type);
            break;
    }
}

let lastRequestIdFromPage: string = "unset";

// ensure content script responds to changes in the page even if it was injected after the page load.
document.addEventListener('DOMContentLoaded', (event) => {
    const port = chrome.runtime.connect({ name: uniquePortName });
    // console.log("KeriAuthCs to SW connected port:", port);

    // register to receive and handle messages from the extension (and indirectly also from the web page)
    port.onMessage.addListener((message: MessageData<unknown>) => handleMessageFromServiceWorker(message, port));


    port.onDisconnect.addListener((p) => {
        // TODO P0 Disconnect should not happen regualarlly?
        console.error("KeriAuthCs: Disconnected from service worker. May need to refresh page. Extension might have: 1) auto-locked, 2) been un/re-installed. Port:", p);

        // TODO P2 implement reconnection logic, but now with a new uniquePortName?
        // handle disconnects, e.g. when a new extension is loaded (but not when a popup is closed)

        // TODO P2 this lastGasp needs work.  See other one in this file
        const lastGasp = {
            type: SwCsMsgType.REPLY,
            requestId: "0726f9ee-e4da-42bb-b3d1-85cada37276c",  // TODO P2 temporary fixed GUID
            source: CsToPageMsgIndicator,
            error: { code: 501, message: "User closed KERI Auth or canceled pending request" },
            rurl: ""
        };

        console.log("KeriAuthCs to Page: lastGasp: ", lastGasp);
        // it's possible the tab also closed first, but try to postMessage anyway

        postMessageToPage<unknown>(lastGasp);
    });

    /*
    chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
        console.log("KeriAuthCs: Extension app is unloading, performing cleanup...");
        if (message.type === "extensionUnloading") {
            console.log("KeriAuthCs: Extension app is unloading, performing cleanup...");
            // Handle cleanup or any actions in the content script
        }
    });
    */

    // Send a hello message to the service worker (versus waiting on a triggering message from the page)
    // TODO P3 use a constructor for the message object
    const helloMsg: ICsSwMsg = { type: CsSwMsgType.SIGNIFY_EXTENSION };
    console.log("KeriAuthCs to SW:", helloMsg);
    port.postMessage(helloMsg);

    // Delay call of advertiseToPage so that polaris-web module to be loaded and ready to receive the message.
    // TODO P3 hack - find a more deterministic approach vs delay?
    setTimeout(advertiseToPage, 500);
});

/*
// Handle messages from the page
*/
function handleWindowMessage(event: MessageEvent<EventData>, portWithSw: chrome.runtime.Port) {
    // console.log("KeriAuthCs handleWindowMessage event:", event);

    // Check if the payload is sent from the current window and is safe to process
    if (window.location.href.indexOf(event.origin) !== 0 && event.source !== window) {
        // Reject likely malicious messages, such as those that might be sent by a malicious extension (Cross-Extension Communication).
        console.warn('KeriAuthCs ignoring potentially malicious message event:', event);
        return;
    }

    // Ignore messages from Cs intended for the Page
    if (event.data.source == CsToPageMsgIndicator) {
        // console.log("KeriAuthCs ignoring message from source, event: ", event.data.source, event);
        return;
    }

    console.log("KeriAuthCs from page: ", event.data);
    try {
        switch (event.data.type) {
            case CsSwMsgType.SIGNIFY_EXTENSION:
                // Note, the signify-extension notification from page is effectively handled (or premptively assumed) earlier in the code, in the advertiseToPage function
                console.log("KeriAuthCs from page ignored: {t}", event.data.type);
                break;
            case CsSwMsgType.SIGN_REQUEST:
            case CsSwMsgType.SIGNIFY_AUTHORIZE:
            case CsSwMsgType.SELECT_AUTHORIZE_CREDENTIAL:
            case CsSwMsgType.SELECT_AUTHORIZE_AID:
                try {
                    if (event.data.payload?.headers) {
                        // TODO P3 create a headers print utility function
                        console.log("KeriAuthCs from page payload headers: ");
                        const hs: Headers = event.data.payload?.headers;
                        for (const pair of hs.entries()) {
                            console.log(` ${pair[0]}: ${pair[1]}`);
                        }
                    }
                    console.log(`KeriAuthCs to SW:`, event.data);
                    portWithSw.postMessage(event.data);
                } catch (error) {
                    console.error("KeriAuthCs to SW: error sending message {event.data} {e}:", event.data, error);
                    return;
                }
                break;

            case CsSwMsgType.CONFIGURE_VENDOR:
                console.info(`KeriAuthCs from page: ${event.data.type} not implemented`);
                break;

            case "/signify/get-session-info":
            case "/signify/clear-session":
                console.info(`KeriAuthCs: ${event.data.type} not implemented`);
                // TODO P2 implement sessions?
                const msg: KeriAuthMessageData<AuthorizeResult> = {
                    type: "/signify/reply",
                    requestId: lastRequestIdFromPage,
                    payload: {},
                    error: "KERIAuthCs: sessions not supported",
                    source: CsToPageMsgIndicator,
                }
                postMessageToPage<KeriAuthMessageData<AuthorizeResult>>(msg);
                break;
            default:
                console.error("KeriAuthCs from page: handler not implemented for:", event.data);
                break;
        }
        // remember the last RequestId, in case of timeout or cancellation
        lastRequestIdFromPage = event?.data?.requestId;
        console.log("KeriAuthCs remembered RequestId: ", lastRequestIdFromPage);
    } catch (error) {
        console.error("KeriAuthCs from page: error in handling event: ", event.data, "Extension may have been reloaded. Try reloading page.", "Error:", error)
    }
};