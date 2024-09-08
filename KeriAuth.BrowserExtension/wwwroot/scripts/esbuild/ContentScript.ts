/// <reference types="chrome" />

// This ContentScript is inserted into pages after a user provided permission for the site (after having clicked on the extension action button)
// The purpose of the ContentScript is primarily to shuttle messages from a web page to/from the extension service-worker.

// Define types for message events and chrome message
interface ChromeMessage {
    type: string;
    subtype?: string;
    [key: string]: any;
}

// Get the current origin
const currentOrigin = window.location.origin;

interface EventData {
    type: string;
    [key: string]: any;
}

import { ICsSwMsgSelectIdentifier, CsSwMsgType, IExCsMsgHello, SwCsMsgType, ISwCsMsg, ICsSwMsg } from "../es6/ExCsInterfaces.js";
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


// Signing related types from signify-browser-extension config/types.ts
interface ISignin {
    id: string;
    domain: string;
    identifier?: {
        name?: string;
        prefix?: string;
    };
    credential?: ICredential;
    createdAt: number;
    updatedAt: number;
    autoSignin?: boolean;
}
interface ICredential {
    issueeName: string;
    ancatc: string[];
    sad: { a: { i: string }; d: string };
    schema: {
        title: string;
        credentialType: string;
        description: string;
    };
    status: {
        et: string;
    };
    cesr?: string;
}

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

//interface SignifyExtensionMessage extends BaseCsPageMessage {
//    type: typeof PAGE_POST_TYPE.SIGNIFY_EXT
//    data: {
//        extensionId: string
//    };
//}

// Union type for all possible messages that can be sent to the web page
// type PageMessage = SignifyExtensionMessage | SignifySignatureMessage | SignifyAutoSigninMessage;

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

const CsToPageMsgIndicator = "KeriAuthCs";

function postMessageToPage<T>(msg2: T): void {
    //const msg: BaseCsPageMessage = {
    //    source: CsToPageMsgIndicator,
    //    type: type,
    //    data: data,
    //    payload: payload,
    //    requestId: requestId,
    //    error: error
    //};
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
            window.addEventListener("message", (event: MessageEvent<any>) => handleWindowMessage(event, port));
            break;
        case SwCsMsgType.REPLY:
            console.log("EE2.1.1.1");
            const msg: MessageData<AuthorizeResult> = {
                type: message.type,
                requestId: message.requestId,
                payload: message.payload,
                error: message.error

            }
    
            postMessageToPage<MessageData<AuthorizeResult>>(msg);
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

//function parseTypeValue(jsonString: string): string | null {
//    try {
//        const parsedObject = JSON.parse(jsonString);

//        // Check if the parsed object is an object and contains the "type" key
//        if (typeof parsedObject === 'object' && parsedObject !== null && 'type' in parsedObject) {
//            const typeValue = parsedObject.type;

//            // Check if the type value is a string
//            if (typeof typeValue === 'string') {
//                return typeValue;
//            } else {
//                console.error('The "type" key is present but its value is not a string.');
//                return null;
//            }
//        } else {
//            console.error('The parsed object does not contain the "type" key or is not a valid object.');
//            return null;
//        }
//    } catch (error) {
//        console.error('Failed to parse JSON:', error.message);
//        return null;
//    }
//}

/*
// Handle messages from the page
*/
function handleWindowMessage(event: MessageEvent<EventData>, portWithSw: chrome.runtime.Port) {

    // TODO EE tmp debug
    console.log("KeriAuthCs handleWindowMessage event:", event);

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

    console.log("EE1");
    console.log("KeriAuthCs received message event.data:", event.data);
    try {
        switch (event.data.type) {
            // case "/signify/reply":  // TODO needed? If so, refactor to PAGE_EVENT_TYPE.REPLY
            case "signify-extension":  // TODO needed? If so, refactor to PAGE_EVENT_TYPE.SIGNIFY_EXTENSION
                // Note, the signify-extension notification from page is effectively haneled earlier in the code, in the advertiseToPage function
                console.info("KeriAuthCs message received intentionally ignored of type, event: ", event.data.type, event);
                break;
            case PAGE_EVENT_TYPE.SIGNIFY_AUTHORIZE:
                console.log("EE1.1");
                try {
                    // const msg = JSON.stringify(event.data);  // assumes no BigInt or other complext content
                    // TODO EE! tmp
                    // if (Math.random() >= 0.1) {
                        portWithSw.postMessage(event.data);

                    //} else {
                    //    // TODO EE! tmp
                    //    if (Math.random() >= 0.5) {
                    //        const fakeError = "fake error" + event.data.requestId;
                    //        postMessageToPage("/signify/reply", null, null, event.data.requestId, fakeError);

                    //    } else {
                    //        // return an identifier or credential as defined in polaris-web/src/client.ts
                    //        if (Math.random() >= 0.5) {
                    //            postMessageToPage("/signify/reply", null, { identifier: { prefix: "asdf" } }, event.data.requestId, null);
                    //        } else {
                    //            postMessageToPage("/signify/reply", null, { credential: { raw: null, cesr: "hail cesr" } }, event.data.requestId, null);
                    //        }
                    //    }
                    //}

                } catch (error) {
                    // TODO refactor to common postMessage wrapper
                    console.error("KeriAuthCs to SW: error converting page event data to JSON or sending message:", error);
                    return;
                }
                break;
            case PAGE_EVENT_TYPE.SIGN_REQUEST:
                try {
                    // const msg = JSON.stringify(event.data);  // assumes no BigInt or other complext content
                    // TODO EE! tmp
                    // if (Math.random() >= 0.1) {
                        portWithSw.postMessage(event.data);
                    //} else {
                    //    // TODO EE! tmp
                    //    if (Math.random() >= 0.5) {
                    //        const fakeError = "fake error" + event.data.requestId;
                    //        postMessageToPage("/signify/reply", null, null, event.data.requestId, fakeError);

                    //    } else {
                    //        // TODO EE! tmp2 return a SignDataResult object as defined in polaris-web/src/client.ts
                    //        postMessageToPage("/signify/reply", null, { aid: "asdf", items: [{ data: "asdfasdf_from original request", signature: "sigsig" }] }, event.data.requestId, null);
                    //    }
                    //}

                } catch (error) {
                    // TODO refactor to common postMessage wrapper
                    console.error("KeriAuthCs to SW: error converting page event data to JSON or sending message:", error);
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