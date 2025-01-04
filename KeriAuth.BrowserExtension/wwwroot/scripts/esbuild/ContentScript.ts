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
    CsSwMsgEnum,
    SwCsMsgEnum,
    ICsSwMsg,
    CsTabMsgTag,
    CsTabMsgData
} from "../es6/ExCsInterfaces.js";

import * as PW from "../types/polaris-web-client";

/*
 * This section is evaluated on document-start, as specified in the extension manifest
 */

// TODO P2 clarify naming consistency and use of: tab, page (including DOM and properties), document (high-level properties), and DOM

// Unique port name for communications between the content script and the extension
let uniquePortName: string;
let portWithSw: chrome.runtime.Port | null;

// Add a listener for messages and create port with SW
console.info("KeriAuthCs adding message listener and creating port with SW");
window.addEventListener("message", (event: MessageEvent<EventData>) => handleWindowMessage(event));
createPortWithSw();

// Observe URL changes in an SPA.Just log it for now to help with debugging issues.
window.addEventListener('popstate', () => {
    console.info("KeriAuthCs popstate change:", window.location.href);
});

// Add listener for when DOMContentLoaded
document.addEventListener('DOMContentLoaded', (event) => {
    console.info(`KeriAuthCs ${event.type} ${window.location.href}`);
});


/*
 * Generate a unique and unguessable identifier for the port name for communications between the content script and the extension
 */
function generateUniqueIdentifier(): string {
    const array = new Uint32Array(4);
    // TODO P4 consider randumUUID() instead
    window.crypto.getRandomValues(array);
    return Array.from(array, dec => ('00000000' + dec.toString(16)).slice(-8)).join('-');
}

/*
 * 
 */
function postMessageToPage<T>(msg: T): void {
    console.log("KeriAuthCs to tab:", (msg as CsTabMsgData<T>).type, msg);
    window.postMessage(msg, currentOrigin);
}

/*
 * Handle messages from SW
 */
function handleMsgFromSW(message: PW.MessageData<unknown>): void {
    if (!message.type) {
        console.error("KeriAuthCs from SW: type not found in message:", message);
        return;
    }

    console.info(`KeriAuthCs from SW: ${message.type}`, message);
    switch (message.type) {
        case SwCsMsgEnum.READY:
            break;

        case SwCsMsgEnum.REPLY:
            const msg: CsTabMsgData<PW.AuthorizeResult> = {
                source: CsTabMsgTag,
                type: message.type,
                requestId: message.requestId,
                payload: message.payload,
                error: message.error,
            }
            postMessageToPage<CsTabMsgData<PW.AuthorizeResult>>(msg);
            break;

        case SwCsMsgEnum.CANCELED:
            const msg2: CsTabMsgData<PW.AuthorizeResult> = {
                source: CsTabMsgTag,
                type: message.type,
                requestId: message.requestId,
                payload: null,
                error: "KERI Auth: User canceled or operation timed out.",
            }
            postMessageToPage<CsTabMsgData<PW.AuthorizeResult>>(msg2);
            break;

        default:
            console.error("KeriAuthCs handler not implemented for message type:", message.type);
            break;
    }
}



/*
 * 
 */
function createPortWithSw(): void {
    console.log(`KeriAuthCs to SW: creating port`);
    uniquePortName = generateUniqueIdentifier();
    portWithSw = chrome.runtime.connect({ name: uniquePortName });
    console.log("KeriAuthCs to SW connected port:", portWithSw);

    // register to receive and handle messages from the extension (and indirectly also from the web page)
    portWithSw.onMessage.addListener((message: PW.MessageData<unknown>) => handleMsgFromSW(message));

    portWithSw.onDisconnect.addListener((p) => {
        // disconnect will typically happen when the service-worker becomes inactive
        console.info("KeriAuthCs Port with service-worker was disconnected, likely due to SW going inactive.");
        portWithSw = null;
    });

    // Send a ping message to the service worker to help complete the setup of connection port
    const initMsg: ICsSwMsg = { type: CsSwMsgEnum.INIT };
    console.log("KeriAuthCs to SW Init:", initMsg);
    portWithSw.postMessage(initMsg);
}

/*
 * Assure port with SW exists, then postMessage
 */
function assurePortAndSend(msg: any) {
    if (portWithSw === null) {
        console.info(`KeriAuthCs re-createPortWithCs()`);
        createPortWithSw();
    }
    console.info(`KeriAuthCs to SW postMessage:`, msg);
    portWithSw.postMessage(msg);
}

/*
 * Handle messages from the page
 */
function handleWindowMessage(event: MessageEvent<EventData>) {

    if (event === undefined) {
        return;
    }

    // Ignore messages not sent from current window
    if (window.location.href.indexOf(event.origin) !== 0 && event.source !== window) {
        // console.info('KeriAuthCs from tab: ignoring message event from other window:', event.data);
        return;
    }

    // Ignore messages from Cs sent to the Tab (instead of from Tab)
    if (event.data.source == CsTabMsgTag) {
        // console.info("KeriAuthCs ignoring message from source, event data: ", event.data);
        return;
    }

    // handle messages from current tab
    console.info(`KeriAuthCs from tab:`, event.data.type, event.data,);
    try {
        const requestId = (event.data as PW.MessageData<any>).requestId;
        switch (event.data.type) {
            case CsSwMsgEnum.POLARIS_SIGNIFY_EXTENSION_CLIENT:
                const extensionMessage2: any = {
                    source: CsTabMsgTag,
                    type: CsSwMsgEnum.POLARIS_SIGNIFY_EXTENSION,
                    data: { extensionId: chrome.runtime.id },
                    requestId: requestId
                }
                postMessageToPage<unknown>(extensionMessage2);
                break;

            case CsSwMsgEnum.POLARIS_SIGNIFY_EXTENSION:
                const extensionMessage: any = {
                    source: CsTabMsgTag,
                    type: CsSwMsgEnum.POLARIS_SIGNIFY_EXTENSION,
                    data: { extensionId: chrome.runtime.id },
                    requestId: requestId
                }
                postMessageToPage<unknown>(extensionMessage);
                break;

            case CsSwMsgEnum.POLARIS_GET_SESSION_INFO:
                const authorizeArgsMessage2 = event.data as PW.MessageData<PW.AuthorizeArgs>;
                const authorizeResult: PW.AuthorizeResult = {};
                // TODO P2 implement sessions?
                const msg: CsTabMsgData<PW.AuthorizeResult> = {
                    source: CsTabMsgTag,
                    type: SwCsMsgEnum.REPLY,
                    requestId: requestId,
                    error: "KERIAuthCs: sessions not supported",  // note that including error string appears to be processed as null in polaris-web
                };
                postMessageToPage<CsTabMsgData<PW.AuthorizeResult>>(msg);
                break;

            case CsSwMsgEnum.POLARIS_CONFIGURE_VENDOR:
                const configureVendorArgsMessage = event.data.payload as PW.MessageData<PW.ConfigureVendorArgs>;
                console.info(`KeriAuthCs ${event.data.type} not implemented`, configureVendorArgsMessage);
                const msg3: CsTabMsgData<PW.AuthorizeResult> = {
                    source: CsTabMsgTag,
                    type: SwCsMsgEnum.REPLY,
                    requestId: requestId,
                    // TODO P2 the type definition doesn't appear to handled what may used in signify-browser-extension, e.g.:
                    // error: { code: 503, message: error?.message },
                    error: "KERIAuthCs: configure-vendor not supported",  // note that including error string appears to be processed as null in polaris-web
                };
                postMessageToPage<CsTabMsgData<PW.AuthorizeResult>>(msg3);
                break;

            case CsSwMsgEnum.POLARIS_SIGNIFY_AUTHORIZE:
            case CsSwMsgEnum.POLARIS_SELECT_AUTHORIZE_CREDENTIAL:
            case CsSwMsgEnum.POLARIS_SELECT_AUTHORIZE_AID:
            case CsSwMsgEnum.POLARIS_SIGN_REQUEST:
                try {
                    const authorizeRequestMessage = event.data as PW.MessageData<PW.AuthorizeArgs>;
                    console.info(`KeriAuthCs ${authorizeRequestMessage.type}:`, authorizeRequestMessage);

                    // TODO P2 this could be simplified with the correct type above?
                    // only relevant for POLARIS_SIGN_REQUEST ?
                    if (event.data.payload?.headers) {
                        console.log("KeriAuthCs from tab payload headers: ");
                        const hs: Headers = event.data.payload?.headers;
                        for (const pair of hs.entries()) {
                            console.log(` ${pair[0]}: ${pair[1]}`);
                        }
                    }
                    assurePortAndSend(event.data);
                } catch (error) {
                    console.error("KeriAuthCs to SW: error sending message {event.data} {e}:", event.data, error);
                    return;
                }
                break;

            case CsSwMsgEnum.POLARIS_CLEAR_SESSION:
                const authorizeArgsMessage3 = event.data as PW.MessageData<PW.AuthorizeArgs>;
                // Although sessions are not implemented, we can respond as expected when Clear is requested
                const clearResult: CsTabMsgData<PW.AuthorizeResult> = {
                    source: CsTabMsgTag,
                    type: SwCsMsgEnum.REPLY, // type: "tab",
                    requestId: requestId,
                    payload: null
                }
                postMessageToPage<CsTabMsgData<PW.AuthorizeResult>>(clearResult);
                break;

            case CsSwMsgEnum.POLARIS_CREATE_DATA_ATTESTATION:
                const createDataAttestationMessage = event.data as PW.MessageData<PW.CreateCredentialArgs>;
                console.info("KeriAuthCs handler not implemented for:", event.data.type, createDataAttestationMessage);
                break;

            case CsSwMsgEnum.POLARIS_GET_CREDENTIAL:
                console.info("KeriAuthCs handler not implemented for:", event.data.type, event.data);
                break;

            case CsSwMsgEnum.POLARIS_SIGN_DATA:
                const signDataArgsMsg = event.data as PW.MessageData<PW.SignDataArgs>;
                console.info("KeriAuthCs handler not implemented for:", signDataArgsMsg.type, signDataArgsMsg);
                break;

            default:
                console.info("KeriAuthCs handler not implemented for:", event.data.type, event.data);
                break;
        }
    } catch (error) {
        // set at info level because its not unusal for the ContentScript to be injected into an unsupported page
        console.info("KeriAuthCs error in handling event: ", event.data, "Extension may have been reloaded. Try reloading page.", "Error:", error)
    }
};