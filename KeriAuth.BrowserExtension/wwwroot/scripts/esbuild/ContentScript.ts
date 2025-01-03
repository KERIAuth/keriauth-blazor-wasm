/// <reference types="chrome" />

// This ContentScript is inserted into tabs after a user provided permission for the site (after having clicked on the extension action button)
// The purpose of the ContentScript is primarily to shuttle messages from a web page to/from the extension service-worker.

const currentOrigin = window.location.origin;

// windows message event data
// TODO P2 better imported form somewhere?
interface EventData {
    type: string;
    [key: string]: any;
}

import {
    CsSwMsgType,
    IExCsMsgHello,
    SwCsMsgType,
    ISwCsMsg,
    ICsSwMsg,
    CsToPageMsgIndicator,
    ISignin,
    ICredential,
    KeriAuthToPolarisMessageData
} from "../es6/ExCsInterfaces.js";

import * as PolarisWeb from "../types/polaris-web-client";

/*
 * Generate a unique and unguessable identifier for the port name for communications between the content script and the extension
 */
function generateUniqueIdentifier(): string {
    const array = new Uint32Array(4);
    window.crypto.getRandomValues(array); // TODO P4 consider randumUUID() instead
    return Array.from(array, dec => ('00000000' + dec.toString(16)).slice(-8)).join('-');
}

// Create the unique port name for communications between the content script and the extension
const uniquePortName: string = generateUniqueIdentifier();

/*
 * 
 */
function postMessageToPage<T>(msg: T): void {
    console.log("KeriAuthCs to tab:", (msg as PolarisWeb.MessageData).type, msg);
    window.postMessage(msg, currentOrigin);
}

/*
 * Handle messages from the extension
 */
function handleMessageFromServiceWorker(message: PolarisWeb.MessageData<unknown>): void {
    if (!message.type) {
        console.error("KeriAuthCs from Sw: type not found in message:", message);
        return;
    }

    console.info(`KeriAuthCs from Sw: ${message.type}`, message);
    switch (message.type) {
        case SwCsMsgType.HELLO: // TODO P0 Pong
            window.addEventListener("message", (event: MessageEvent<EventData>) => handleWindowMessage(event));
            break;
        case SwCsMsgType.REPLY:
            const msg: KeriAuthToPolarisMessageData<PolarisWeb.AuthorizeResult> = {
                source: CsToPageMsgIndicator,
                type: message.type,
                requestId: message.requestId,
                payload: message.payload,
                error: message.error,
            }
            postMessageToPage<KeriAuthToPolarisMessageData<PolarisWeb.AuthorizeResult>>(msg);
            break;

        case SwCsMsgType.CANCELED:
            const msg2: KeriAuthToPolarisMessageData<PolarisWeb.AuthorizeResult> = {
                source: CsToPageMsgIndicator,
                type: message.type,
                requestId: message.requestId,
                payload: null,
                error: "KERI Auth: User canceled or operation timed out.",
            }
            postMessageToPage<KeriAuthToPolarisMessageData<PolarisWeb.AuthorizeResult>>(msg2);

            break;

        default:
            console.error("KeriAuthCs: handler not implemented for message type:", message.type);
            break;
    }
}

let portWithSw: chrome.runtime.Port;

/*
 * 
 */
function createPortWithSw(withHandshake: boolean): void {
    console.log(`KeriAuthCs to SW: (re-)creating port`);
    portWithSw = chrome.runtime.connect({ name: uniquePortName });
    console.log("KeriAuthCs to SW connected port:", portWithSw);

    // register to receive and handle messages from the extension (and indirectly also from the web page)
    portWithSw.onMessage.addListener((message: PolarisWeb.MessageData<unknown>) => handleMessageFromServiceWorker(message));

    portWithSw.onDisconnect.addListener((p) => {
        // disconnect will typically happen when the service-worker becomes inactive
        console.info("KeriAuthCs: Port with service-worker was disconnected, likely due to SW going inactive.");
        portWithSw = null;
    });

    if (withHandshake) {
        // Send a hello message to the service worker (versus waiting on a triggering message from the page)
        // TODO P3 use a constructor for the message object
        const helloMsg: ICsSwMsg = { type: CsSwMsgType.POLARIS_SIGNIFY_EXTENSION }; // TODO P0 Ping to set up connections
        console.log("KeriAuthCs to SW:", helloMsg);
        portWithSw.postMessage(helloMsg);
        // Delay call of advertiseToPage so that polaris-web module to be loaded and ready to receive the message.
        // TODO P3 hack - find a more deterministic approach vs delay?
        // setTimeout(advertiseToPage, 500);
    }
}

/*
 * 
 */
document.addEventListener('DOMContentLoaded', (event) => {
    console.info("CS DOMContentLoaded");
    window.addEventListener("message", (event: MessageEvent<EventData>) => handleWindowMessage(event));
    createPortWithSw(true);
    return;
});

/*
// Handle messages from the page
*/
function handleWindowMessage(event: MessageEvent<EventData>) {
    // Check if the payload is sent from the current window and is safe to process
    if (window.location.href.indexOf(event.origin) !== 0 && event.source !== window) {
        // Reject likely malicious messages, such as those that might be sent by a malicious extension (Cross-Extension Communication).
        // set at info level because its not unusal for the ContentScript to be injected into an unsupported page
        console.warn('KeriAuthCs from tab: ignoring potentially malicious message event:', event.data);
        return;
    }

    // Ignore messages from Cs intended for the Page
    if (event.data.source == CsToPageMsgIndicator) {
        // console.log("KeriAuthCs ignoring message from source, event: ", event.data.source, event);
        return;
    }

    console.log("KeriAuthCs from tab: ", event.data.type, event.data);

    try {
        const requestId = (event.data as PolarisWeb.MessageData<any>).requestId;
        switch (event.data.type) {
            case CsSwMsgType.POLARIS_SIGNIFY_EXTENSION:
            case CsSwMsgType.POLARIS_SIGNIFY_EXTENSION_CLIENT:
                const extensionMessage: any = {
                    source: CsToPageMsgIndicator,
                    type: CsSwMsgType.POLARIS_SIGNIFY_EXTENSION,
                    data: { extensionId: chrome.runtime.id },
                    requestId: requestId
                }
                postMessageToPage<unknown>(extensionMessage);
                break;

            case CsSwMsgType.POLARIS_CONFIGURE_VENDOR:
                const configureVendorArgsMessage = event.data.payload as PolarisWeb.MessageData<PolarisWeb.ConfigureVendorArgs>;
                console.info(`KeriAuthCs: ${event.data.type} not implemented`, configureVendorArgsMessage);
                break;

            case CsSwMsgType.POLARIS_SIGNIFY_AUTHORIZE:
            case CsSwMsgType.POLARIS_SELECT_AUTHORIZE_CREDENTIAL:
            case CsSwMsgType.POLARIS_SELECT_AUTHORIZE_AID:
            case CsSwMsgType.POLARIS_SIGN_REQUEST:
                try {
                    const authorizeRequestMessage = event.data as PolarisWeb.MessageData<PolarisWeb.AuthorizeArgs>;
                    console.info(`KeriAuthCs: ${authorizeRequestMessage.type}:`, authorizeRequestMessage);

                    // TODO this could be simplified with the correct type above?
                    if (event.data.payload?.headers) {
                        console.log("KeriAuthCs from tab payload headers: ");
                        const hs: Headers = event.data.payload?.headers;
                        for (const pair of hs.entries()) {
                            console.log(` ${pair[0]}: ${pair[1]}`);
                        }
                    }
                    console.log(`KeriAuthCs to SW:`, event.data);

                    // since the portWithSw may have disconnected when the service-worker transitioned to inactive state, we may need to recreate it here
                    if (portWithSw === null) {
                        createPortWithSw(true);
                    }
                    portWithSw.postMessage(event.data);

                } catch (error) {
                    console.error("KeriAuthCs to SW: error sending message {event.data} {e}:", event.data, error);
                    return;
                }
                break;

            case CsSwMsgType.POLARIS_GET_SESSION_INFO:
                const authorizeArgsMessage2 = event.data as PolarisWeb.MessageData<PolarisWeb.AuthorizeArgs>;

                // TODO P2 implement sessions?
                const msg: KeriAuthToPolarisMessageData<PolarisWeb.AuthorizeResult> = {
                    source: CsToPageMsgIndicator,
                    type: SwCsMsgType.REPLY,
                    requestId: requestId,
                    // source: CsToPageMsgIndicator,
                    error: "KERIAuthCs: sessions not supported",
                };
                postMessageToPage<KeriAuthToPolarisMessageData<PolarisWeb.AuthorizeResult>>(msg);
                break;

            case CsSwMsgType.POLARIS_CLEAR_SESSION:
                const authorizeArgsMessage3 = event.data as PolarisWeb.MessageData<PolarisWeb.AuthorizeArgs>;
                // TODO P3 although sessions are not implemented, we can respond as expected when Clear is requested
                const clearResult: KeriAuthToPolarisMessageData<PolarisWeb.AuthorizeResult> = {
                    source: CsToPageMsgIndicator,
                    type: "tab",
                    requestId: requestId,
                    payload: null
                }
                postMessageToPage<KeriAuthToPolarisMessageData<PolarisWeb.AuthorizeResult>>(clearResult);
                break;

            case CsSwMsgType.POLARIS_CREATE_DATA_ATTESTATION:
                const createDataAttestationMessage = event.data as PolarisWeb.MessageData<PolarisWeb.CreateCredentialArgs>;
                console.info("KeriAuthCs: handler not implemented for:", event.data.type, createDataAttestationMessage);
                break;

            case CsSwMsgType.POLARIS_GET_CREDENTIAL:
                console.info("KeriAuthCs: handler not implemented for:", event.data.type, event.data);
                break;

            case CsSwMsgType.POLARIS_SIGN_DATA:
                const signDataArgsMsg = event.data as PolarisWeb.MessageData<PolarisWeb.SignDataArgs>;
                console.info("KeriAuthCs: handler not implemented for:", signDataArgsMsg.type, signDataArgsMsg);
                break;

            default:
                console.info("KeriAuthCs: handler not implemented for:", event.data.type, event.data);
                break;
        }
    } catch (error) {
        // set at info level because its not unusal for the ContentScript to be injected into an unsupported page
        console.info("KeriAuthCs: error in handling event: ", event.data, "Extension may have been reloaded. Try reloading page.", "Error:", error)
    }
};