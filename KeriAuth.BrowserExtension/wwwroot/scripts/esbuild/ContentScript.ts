/// <reference types="chrome" />

// This ContentScript is inserted into pages after a user provided permission for the site (after having clicked on the extension action button)
// The purpose of the ContentScript is primarily to shuttle messages from a web page to/from the extension service-worker.

// Get the current origin
const currentOrigin = window.location.origin;

// windows message event data
interface EventData {
    type: string;
    [key: string]: any;
}

import { ICsSwMsgSelectIdentifier, CsSwMsgType, IExCsMsgHello, SwCsMsgType, ISwCsMsg, ICsSwMsg, CsToPageMsgIndicator, KeriAuthMessageData, ISignin, ICredential } from "../es6/ExCsInterfaces.js";
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
} from "polaris-web/dist/client";
// TODO, or consider using the following instead of the above import
// import * as PolarisWebClient from "polaris-web/dist/client";





// signify-brower-extension compliant page message types
// Note this is called TAB_STATE and others in the signify-browser-extension
// this "const" structure was intentionally used versus an enum, because of CommonJS module system in use.
// TODO above is no longer a constraint, and can move these back to enums?

// page to CS
const PAGE_EVENT_TYPE = Object.freeze({
    VENDOR_INFO: "vendor-info",
    FETCH_RESOURCE: "fetch-resource",
    SELECT_IDENTIFIER: "/signify/authorize/aid",
    SELECT_CREDENTIAL: "/signify/authorize/credential",
    SIGNIFY_AUTHORIZE: "/signify/authorize",
    AUTHORIZE_AUTO_SIGNIN: "/signify/authorize-auto-signin",
    SIGN_REQUEST: "/signify/sign-request",
    CONFIGURE_VENDOR: "/signify/configure-vendor",
    SELECT_AUTO_SIGNIN: "select-auto-signin",
    NONE: "none"
})

// CS to page
const PAGE_POST_TYPE = Object.freeze({
    SIGNIFY_EXT: "signify-extension"
    // SELECT_AUTO_SIGNIN: "select-auto-signin",
    // SIGNIFY_SIGNATURE: "signify-signature",
    // SIGNIFY_SIGNATURE2: "signify-signature"
})

// interfaces for the messages posted to the web page
interface BaseCsPageMessage {
    // version: "0.0.1"
    source: "KeriAuthCs";
    type: string;
    data?: object;
    payload?: object;
    error?: string;
    requestId?: string;
}

// Function to generate a unique and unguessable identifier for the port name for communications between the content script and the extension
function generateUniqueIdentifier(): string {
    const array = new Uint32Array(4);
    window.crypto.getRandomValues(array); // TODO consider randumUUID() instead
    return Array.from(array, dec => ('00000000' + dec.toString(16)).slice(-8)).join('-');
}

// Create the unique port name for communications between the content script and the extension
const uniquePortName: string = generateUniqueIdentifier();

function advertiseToPage(): void {
    // TODO the following should be typed and ideally imported from polaris-web/client.ts
    const msg = {
        type: PAGE_POST_TYPE.SIGNIFY_EXT,
        data: { extensionId: String(chrome.runtime.id) },
    }
    postMessageToPage<unknown>(msg);
}

function postMessageToPage<T>(msg2: T): void {
    console.log("KeriAuthCs to page data:", msg2);
    window.postMessage(msg2, currentOrigin);
}

// Handle messages from the extension
function handleMessageFromServiceWorker(message: BaseCsPageMessage, port: chrome.runtime.Port): void {
    // TODO move this into its own function for readability
    console.log("KeriAuthCs from SW message:", message);

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
                source: CsToPageMsgIndicator
            }
            postMessageToPage<KeriAuthMessageData<AuthorizeResult>>(msg);
            break;
        case "signify-extension":
            console.log("intentionally ignoring type signify-extension here, as it is handled in advertiseToPage function.")
            break;
        case SwCsMsgType.CANCELED:
        default:
            console.error("KeriAuthCs from SW: handler not implemented for message type:", message.type);
            break;
    }
}

// ensure content script responds to changes in the page even if it was injected after the page load.
document.addEventListener('DOMContentLoaded', (event) => {
    const port = chrome.runtime.connect({ name: uniquePortName });
    console.log("KeriAuthCs to SW connected port:", port);

    // register to receive and handle messages from the extension (and indirectly also from the web page)
    port.onMessage.addListener((message: BaseCsPageMessage) => handleMessageFromServiceWorker(message, port));

    // Send a hello message to the service worker (versus waiting on a triggering message from the page)
    // TODO use a constructor for the message object
    const helloMsg: ICsSwMsg = { type: CsSwMsgType.SIGNIFY_EXTENSION };
    console.log("KeriAuthCs to SW:", helloMsg);
    port.postMessage(helloMsg);

    // Delay call of advertiseToPage so that polaris-web module to be loaded and ready to receive the message.
    // TODO hack - find a more deterministic approach vs delay?
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
        console.log("KeriAuthCs ignoring message from ", event.data.source);
        return;
    }

    console.log("KeriAuthCs received message event.data:", event.data);
    try {
        switch (event.data.type) {
            // case "/signify/reply":  // TODO needed? If so, refactor to PAGE_EVENT_TYPE.REPLY
            case "signify-extension":  // TODO needed? If so, refactor to PAGE_EVENT_TYPE.SIGNIFY_EXTENSION
                // Note, the signify-extension notification from page is effectively haneled earlier in the code, in the advertiseToPage function
                console.info("KeriAuthCs message received intentionally ignored of type, event: ", event.data.type, event);
                break;
            case PAGE_EVENT_TYPE.SIGNIFY_AUTHORIZE:
                try {
                    portWithSw.postMessage(event.data);
                } catch (error) {
                    // TODO refactor to common postMessage wrapper
                    console.error("KeriAuthCs to SW: error converting page event data to JSON or sending message:", error);
                    return;
                }
                break;
            case PAGE_EVENT_TYPE.SIGN_REQUEST:
                try {
                    portWithSw.postMessage(event.data);
                } catch (error) {
                    console.error("KeriAuthCs to SW: error sending message {event.data} {e}:", event.data, error);
                    return;
                }
                break;
            case PAGE_EVENT_TYPE.SELECT_CREDENTIAL:
            case PAGE_EVENT_TYPE.SELECT_IDENTIFIER:
            case PAGE_EVENT_TYPE.SELECT_AUTO_SIGNIN:
            case PAGE_EVENT_TYPE.NONE:
            case PAGE_EVENT_TYPE.VENDOR_INFO:
            case PAGE_EVENT_TYPE.FETCH_RESOURCE:
            default:
                console.error("KeriAuthCs from page: handler not yet implemented for:", event.data);
                break;
        }
    } catch (error) {
        console.error("KeriAuthCs from page: error in handling event: ", event.data, "Extension may have been reloaded. Try reloading page.", "Error:", error)
    }
};