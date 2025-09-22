/// <reference types="chrome-types" />

// This ContentScript is inserted into tabs after a user provided permission for the site (after having clicked on the extension action button)
// The purpose of the ContentScript is primarily to shuttle messages from a web page to/from the extension BackgroundWorker.

import {
    type ICsPageMsgData,
    type ICsPageMsgDataData,
    type ICsSwMsg,
    CsPageMsgTag,
    CsSwMsgEnum,
    SwCsMsgEnum
} from '../es6/ExCsInterfaces';

import type * as PW from '../types/polaris-web-client';

// Type for messages from the page (combines polaris-web messages and CS-specific messages)
// Some messages may have a 'source' property to identify origin
type PageMessageData =
    (PW.MessageData<PW.AuthorizeArgs> & { source?: string })
    | (PW.MessageData<PW.CreateCredentialArgs> & { source?: string })
    | (PW.MessageData<PW.SignDataArgs> & { source?: string })
    | (PW.MessageData<PW.SignRequestArgs> & { source?: string })
    | (PW.MessageData<PW.ConfigureVendorArgs> & { source?: string })
    | (PW.MessageData<null> & { source?: string })
    | ICsPageMsgData<unknown>;

/*
 * This section is evaluated on document-start, as specified in the extension manifest
 */
console.groupCollapsed('KeriAuthCs initializing');
const currentOrigin = window.location.origin;
console.log('KeriAuthCs currentOrigin:', currentOrigin);
console.log('KeriAuthCs extension:', chrome.runtime.getManifest().name, chrome.runtime.getManifest().version_name, chrome.runtime.id);

// Add a listener for messages and create port with SW
window.addEventListener('message', (event: MessageEvent<PageMessageData>) => handleWindowMessage(event));

// Unique port name for communications between the content script and the extension
const uniquePortName: string = generateUniqueIdentifier();
let portWithSw: chrome.runtime.Port | null = null;

// Initialize port connection with error handling
try {
    portWithSw = chrome.runtime.connect(chrome.runtime.id, { name: uniquePortName });
    createPortWithSw();
} catch (error) {
    console.error('KeriAuthCs: Failed to create initial port connection:', error);
    // Will attempt to reconnect when first message needs to be sent
}

// Observe and log URL changes in any SPA page. May be helpful for debugging potential issues.
window.addEventListener('popstate', (event) => {
    console.info(`KeriAuthCs ${event.type} ${window.location.href}`);
});

// Add listener for when DOMContentLoaded. Log if helpful for debugging issues.
document.addEventListener('DOMContentLoaded', (event) => {
    console.info(`KeriAuthCs ${event.type} ${window.location.href}`);
});

console.groupEnd();

/**
 * Generate a unique and unguessable identifier for the port name for communications between the content script and the extension
 * @returns A cryptographically random string identifier
 */
function generateUniqueIdentifier(): string {
    const array = new Uint32Array(4);
    // TODO P4 consider randomUUID() instead
    window.crypto.getRandomValues(array);
    return Array.from(array, dec => (`00000000${dec.toString(16)}`).slice(-8)).join('-');
}

/**
 * Send a message from the content script to the web page
 * @param msg The message to send to the page, must have a 'type' property
 */
function postMessageToPage<T>(msg: T): void {
    console.log(`KeriAuthCs→Page: ${(msg as ICsPageMsgData<T>).type}`, { msg });
    window.postMessage(msg, currentOrigin);
}

/**
 * Handle messages received from the Service Worker (BackgroundWorker)
 * Routes responses back to the web page with appropriate formatting
 * @param message Message from the Service Worker following polaris-web protocol
 */
function handleMsgFromSW(message: PW.MessageData<unknown>): void {
    if (!message.type) {
        console.error('KeriAuthCs←SW: type not found in message:', message);
        return;
    }

    console.groupCollapsed(`KeriAuthCs←SW: ${message.type}`);
    console.log(message);
    switch (message.type) {
        case SwCsMsgEnum.READY:
            break;

        case SwCsMsgEnum.REPLY:
            if (message.error) {
                const errorMsg: ICsPageMsgData<null> = {
                    source: CsPageMsgTag,
                    type: message.type,
                    requestId: message.requestId,
                    error: String(message.error)
                };
                postMessageToPage<ICsPageMsgData<null>>(errorMsg);
            } else {
                const msg: ICsPageMsgData<PW.AuthorizeResult> = {
                    source: CsPageMsgTag,
                    type: message.type,
                    requestId: message.requestId,
                    payload: message.payload as PW.AuthorizeResult
                };
                postMessageToPage<ICsPageMsgData<PW.AuthorizeResult>>(msg);
            }
            break;

        case SwCsMsgEnum.REPLY_CANCELED:
            {
                const canceledMsg: ICsPageMsgData<null> = {
                    source: CsPageMsgTag,
                    type: SwCsMsgEnum.REPLY,
                    requestId: message.requestId,
                    error: message.error
                };
                postMessageToPage<ICsPageMsgData<null>>(canceledMsg);
            };
            break;

        case SwCsMsgEnum.APP_CLOSED:
            if (message.requestId) {
                const appClosedMsg: ICsPageMsgData<null> = {
                    source: CsPageMsgTag,
                    type: SwCsMsgEnum.REPLY,
                    requestId: message.requestId, // might be null
                    error: message.error
                };
                postMessageToPage<ICsPageMsgData<null>>(appClosedMsg);
            }
            break;

        default:
            console.error(`KeriAuthCs handler not implemented for message type ${message.type}`);
            break;
    }
    console.groupEnd();
}

/**
 * Initialize the port connection with the Service Worker
 * Sets up message and disconnect listeners, sends initial INIT message
 * Assumes portWithSw has already been created via chrome.runtime.connect()
 */
function createPortWithSw(): void {
    console.groupCollapsed('KeriAuthCs→SW: creating port');

    if (!portWithSw) {
        console.error('KeriAuthCs→SW: Port is null, cannot setup listeners');
        return;
    }

    console.log('KeriAuthCs→SW connected port:', portWithSw);

    // register to receive and handle messages from the extension (and indirectly also from the web page)
    portWithSw.onMessage.addListener((message: PW.MessageData<unknown>) => handleMsgFromSW(message));

    portWithSw.onDisconnect.addListener(() => {
        // disconnect will typically happen when the BackgroundWorker becomes inactive
        console.info('KeriAuthCs Port with BackgroundWorker was disconnected, likely due to BackgroundWorker going inactive.');
        // Set to null so assurePortAndSend knows to reconnect
        portWithSw = null;
    });

    // Send a ping message to the BackgroundWorker to help complete the setup of connection port
    const initMsg: ICsSwMsg = { type: CsSwMsgEnum.INIT };
    console.log('KeriAuthCs→SW Init:', initMsg);
    try {
        portWithSw.postMessage(initMsg);
    } catch (error) {
        console.error('KeriAuthCs→SW: Failed to send INIT message:', error);
        // Port may have disconnected already, set to null for reconnection
        portWithSw = null;
    }
    console.groupEnd();
}

/**
 * Ensure port connection exists and send message to Service Worker
 * Automatically reconnects if port was disconnected (e.g., when Service Worker went inactive)
 * @param msg Message to send, either polaris-web protocol or internal CS-SW message
 * @throws Error if unable to connect or send message
 */
function assurePortAndSend(msg: PW.MessageData<unknown> | ICsSwMsg): void {
    // Check if port is null (happens when BackgroundWorker goes inactive and disconnects)
    // Chrome extension ports cannot be reused after disconnection - must create new connection
    if (portWithSw === null) {
        console.info('KeriAuthCs re-creating port connection to BackgroundWorker');
        try {
            portWithSw = chrome.runtime.connect(chrome.runtime.id, { name: uniquePortName });
            createPortWithSw();
        } catch (error) {
            console.error('KeriAuthCs→SW: Failed to reconnect to BackgroundWorker:', error);
            throw error; // Re-throw to let caller handle the error
        }
    }
    console.info('KeriAuthCs→SW: postMessage:', msg);
    try {
        portWithSw.postMessage(msg);
    } catch (error) {
        console.error('KeriAuthCs→SW: Failed to send message:', error);
        // Port may have disconnected, set to null and re-throw
        portWithSw = null;
        throw error;
    }
}

/**
 * Handle messages from the web page and route them to the Service Worker
 * Validates message origin and filters out echo messages from content script
 * @param event Message event from the web page containing polaris-web protocol messages
 */
function handleWindowMessage(event: MessageEvent<PageMessageData>): void {

    if (event === undefined) {
        return;
    }

    // Ignore messages not sent from current window
    if (window.location.href.indexOf(event.origin) !== 0 && event.source !== window) {
        return;
    }

    // Ignore messages from Cs sent to the Tab (instead of from Tab)
    if (event.data.source === CsPageMsgTag) {
        return;
    }

    // handle messages from current page
    console.groupCollapsed(`KeriAuthCs←Page: ${event.data.type}`);
    console.log(event.data);
    try {
        const requestId = event.data.requestId;
        switch (event.data.type) {
            case CsSwMsgEnum.POLARIS_SIGNIFY_EXTENSION_CLIENT: {
                const extensionClientMsg: ICsPageMsgDataData<{ extensionId: string }> = {
                    source: CsPageMsgTag,
                    type: CsSwMsgEnum.POLARIS_SIGNIFY_EXTENSION,
                    data: { extensionId: chrome.runtime.id },
                    requestId
                };
                postMessageToPage<ICsPageMsgDataData<{ extensionId: string }>>(extensionClientMsg);
                break;
            }
            case CsSwMsgEnum.POLARIS_SIGNIFY_EXTENSION: {
                const extensionMessage: ICsPageMsgDataData<{ extensionId: string }> = {
                    source: CsPageMsgTag,
                    type: CsSwMsgEnum.POLARIS_SIGNIFY_EXTENSION,
                    data: { extensionId: chrome.runtime.id },
                    requestId
                };
                postMessageToPage<ICsPageMsgDataData<{ extensionId: string }>>(extensionMessage);
                break;
            }
            case CsSwMsgEnum.POLARIS_GET_SESSION_INFO: {
                // const authorizeArgsMessage2 = event.data as PW.MessageData<PW.AuthorizeArgs>;
                // const authorizeResult: PW.AuthorizeResult = {};
                // TODO P2 implement sessions?
                const sessionInfoMsg: ICsPageMsgData<null> = {
                    source: CsPageMsgTag,
                    type: SwCsMsgEnum.REPLY,
                    requestId,
                    error: 'KERIAuthCs: sessions not supported'
                };
                postMessageToPage<ICsPageMsgData<null>>(sessionInfoMsg);
                break;
            }
            case CsSwMsgEnum.POLARIS_CONFIGURE_VENDOR: {
                const configureVendorArgsMessage = event.data.payload as PW.MessageData<PW.ConfigureVendorArgs>;
                console.info(`KeriAuthCs ${event.data.type} not implemented`, configureVendorArgsMessage);
                const configVendorMsg: ICsPageMsgData<null> = {
                    source: CsPageMsgTag,
                    type: SwCsMsgEnum.REPLY,
                    requestId,
                    error: 'KERIAuthCs: configure-vendor not supported'
                };
                postMessageToPage<ICsPageMsgData<null>>(configVendorMsg);
                break;
            }
            case CsSwMsgEnum.POLARIS_SIGNIFY_AUTHORIZE:
            case CsSwMsgEnum.POLARIS_SELECT_AUTHORIZE_CREDENTIAL:
            case CsSwMsgEnum.POLARIS_SELECT_AUTHORIZE_AID:
            case CsSwMsgEnum.POLARIS_SIGN_REQUEST:
                try {
                    console.info(`KeriAuthCs ${event.data.type}:`, event.data);

                    // Log headers for POLARIS_SIGN_REQUEST
                    if (event.data.type === CsSwMsgEnum.POLARIS_SIGN_REQUEST) {
                        const signRequestMessage = event.data as PW.MessageData<PW.SignRequestArgs>;
                        if (signRequestMessage.payload?.headers) {
                            console.log('KeriAuthCs payload headers: ');
                            const headers = signRequestMessage.payload.headers;
                            for (const [key, value] of Object.entries(headers)) {
                                console.log(` ${key}: ${value}`);
                            }
                        }
                    }
                    assurePortAndSend(event.data);
                } catch (error) {
                    console.error('KeriAuthCs→SW: error sending message {event.data} {e}:', event.data, error);
                    return;
                }
                break;

            case CsSwMsgEnum.POLARIS_CLEAR_SESSION: {
                // const authorizeArgsMessage3 = event.data as PW.MessageData<PW.AuthorizeArgs>;
                // Although sessions are not implemented, we can respond as expected when Clear is requested
                const clearResult: ICsPageMsgData<PW.AuthorizeResult> = {
                    source: CsPageMsgTag,
                    type: SwCsMsgEnum.REPLY, // type: "tab",
                    requestId,
                    payload: undefined
                };
                postMessageToPage<ICsPageMsgData<PW.AuthorizeResult>>(clearResult);
                break;
            }
            case CsSwMsgEnum.POLARIS_CREATE_DATA_ATTESTATION: {
                const createDataAttestationMessage = event.data as PW.MessageData<PW.CreateCredentialArgs>;
                console.info(`KeriAuthCs handler not implemented for ${event.data.type}`, { createDataAttestationMessage });
                break;
            }
            case CsSwMsgEnum.POLARIS_GET_CREDENTIAL:
                console.info(`KeriAuthCs handler not implemented for ${event.data.type}`, event.data);
                break;

            case CsSwMsgEnum.POLARIS_SIGN_DATA: {
                const signDataArgsMsg = event.data as PW.MessageData<PW.SignDataArgs>;
                console.info(`KeriAuthCs handler not implemented for ${signDataArgsMsg.type}`, signDataArgsMsg);
                break;
            }
            default:
                console.info(`KeriAuthCs handler not implemented for ${event.data.type}`, event.data);
                break;
        }
    } catch (error) {
        // set at info level because its not unusal for the ContentScript to be injected into an unsupported page
        console.info('KeriAuthCs error in handling event: ', event.data, 'Extension may have been reloaded. Try reloading page.', 'Error:', error);
    }
    console.groupEnd();
};
