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
function handleMessageFromServiceWorker(message: MessageData<unknown>): void {
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
            window.addEventListener("message", (event: MessageEvent<EventData>) => handleWindowMessage(event));
            break;
        case SwCsMsgType.REPLY:
            const msg: KeriAuthMessageData<AuthorizeResult> = {
                type: message.type,
                requestId: message.requestId, // TODO P2 lastRequestIdFromPage,
                payload: message.payload, // TODO P2 ?? {},
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
            // advertiseToPage();
            break;
        default:
            console.error("KeriAuthCs from SW: handler not implemented for message type:", message.type);
            break;
    }
}

let lastRequestIdFromPage: string = "unset";
let portWithSw: chrome.runtime.Port;

function createPort(withHandshake: boolean): void {
    console.log(`KeriAuthCs to SW: (re-)creating port`);
    portWithSw = chrome.runtime.connect({ name: uniquePortName });
    // console.log("KeriAuthCs to SW connected port:", port);

    // register to receive and handle messages from the extension (and indirectly also from the web page)
    portWithSw.onMessage.addListener((message: MessageData<unknown>) => handleMessageFromServiceWorker(message));

    portWithSw.onDisconnect.addListener((p) => {
        // disconnect will typically happen when the service-worker becomes inactive
        console.info("KeriAuthCs: Port with service-worker was disconnected, likely due to SW going inactive.");
        portWithSw = null;
    });

    if (withHandshake) {
        // Send a hello message to the service worker (versus waiting on a triggering message from the page)
        // TODO P3 use a constructor for the message object
        const helloMsg: ICsSwMsg = { type: CsSwMsgType.SIGNIFY_EXTENSION };
        console.log("KeriAuthCs to SW:", helloMsg);
        portWithSw.postMessage(helloMsg);
        // Delay call of advertiseToPage so that polaris-web module to be loaded and ready to receive the message.
        // TODO P3 hack - find a more deterministic approach vs delay?
        setTimeout(advertiseToPage, 500);
    }
}

// ensure content script responds to changes in the page even if it was injected after the page load.
document.addEventListener('DOMContentLoaded', (event) => {
    createPort(true);
});

/*
// Handle messages from the page
*/
function handleWindowMessage(event: MessageEvent<EventData>) {
    // console.log("KeriAuthCs handleWindowMessage event:", event);

    // Check if the payload is sent from the current window and is safe to process
    if (window.location.href.indexOf(event.origin) !== 0 && event.source !== window) {
        // Reject likely malicious messages, such as those that might be sent by a malicious extension (Cross-Extension Communication).
        // set at info level because its not unusal for the ContentScript to be injected into an unsupported page
        console.info('KeriAuthCs ignoring potentially malicious message event:', event);
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
                        console.log("KeriAuthCs from page payload headers: ");
                        const hs: Headers = event.data.payload?.headers;
                        for (const pair of hs.entries()) {
                            console.log(` ${pair[0]}: ${pair[1]}`);
                        }
                    }
                    console.log(`KeriAuthCs to SW:`, event.data);

                    // since the portWithSw may have disconnected when the service-worker transitioned to inactive state, we may need to recreate it here
                    if (portWithSw === null) {
                        createPort(true);
                    } 
                    portWithSw.postMessage(event.data);

                } catch (error) {
                    console.error("KeriAuthCs to SW: error sending message {event.data} {e}:", event.data, error);
                    return;
                }
                break;

            case CsSwMsgType.CONFIGURE_VENDOR:
                console.info(`KeriAuthCs: ${event.data.type} not implemented`);
                break;

            case "/signify/get-session-info":
            case "/signify/clear-session":
                console.info(`KeriAuthCs: ${event.data.type} not implemented`);
                // TODO P2 implement sessions?
                const msg: KeriAuthMessageData<AuthorizeResult> = {
                    type: SwCsMsgType.REPLY,
                    requestId: lastRequestIdFromPage,
                    source: CsToPageMsgIndicator,
                    error: "KERIAuthCs: sessions not supported",
                };
                postMessageToPage<KeriAuthMessageData<AuthorizeResult>>(msg);
                break;
            default:
                // set at info level because its not unusal for the ContentScript to be injected into an unsupported page
                console.info("KeriAuthCs from page: handler not implemented for:", event.data);
                break;
        }
        // remember the last RequestId, in case of timeout or cancellation
        lastRequestIdFromPage = event?.data?.requestId;
        // console.log("KeriAuthCs remembered RequestId: ", lastRequestIdFromPage);
    } catch (error) {
        // set at info level because its not unusal for the ContentScript to be injected into an unsupported page
        console.info("KeriAuthCs from page: error in handling event: ", event.data, "Extension may have been reloaded. Try reloading page.", "Error:", error)
    }
};