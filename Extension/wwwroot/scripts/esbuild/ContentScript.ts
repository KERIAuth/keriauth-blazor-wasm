/// <reference types="chrome-types" />

// This ContentScript is inserted into tabs after a user provided permission for the site (after having clicked on the extension action button)
// The purpose of the ContentScript is primarily to shuttle messages from a web page to/from the extension BackgroundWorker.

/**
 * High-level flow:
 * Page  --(window.postMessage one-shot)-->  CS  --(port msg)-->  BW
 * Page  <--(window.postMessage reply)---   CS  <--(port msg)--  BW
 */

import {
    type ICsPageMsgData,
    type ICsPageMsgDataData,
    type ICsBwMsg,
    CsPageMsgTag,
    CsBwMsgEnum,
    BwCsMsgEnum
} from '../es6/ExCsInterfaces';

import type * as PW from '../types/polaris-web-client';

// Type for messages from the page (combines polaris-web messages and CS-specific messages)
// Some messages may have a 'source' property to identify origin
type IPageMessageData =
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

// Sentinel pattern to prevent double-injection
(() => {
    const KEY = '__KERIAUTH_CS_INJECTED__';
    if ((globalThis as unknown as Record<string, boolean>)[KEY]) {
        console.log('KeriAuthCs: Already injected, skipping initialization');
        return; // already active, do nothing
    }
    (globalThis as unknown as Record<string, boolean>)[KEY] = true;
})();

console.log('KeriAuthCs initializing');
const currentOrigin = window.location.origin;
console.log('KeriAuthCs currentOrigin:', currentOrigin);
console.log('KeriAuthCs extension:', chrome.runtime.getManifest().name, chrome.runtime.getManifest().version_name, chrome.runtime.id);

// Add a listener for messages and create port with BW
window.addEventListener('message', (event: MessageEvent<IPageMessageData>) => handleWindowMessage(event));

// Respond to ping messages from the service worker to detect if content script is already injected
chrome.runtime.onMessage.addListener((msg, _sender, sendResponse) => {
    if (msg?.type === 'ping') {
        (sendResponse as (response?: unknown) => void)({ ok: true });
        return true; // keep channel open if needed
    }
    // Return false for other messages to allow other listeners to handle them
    return false;
});

// Unique port name for communications between the content script and the extension
const uniquePortName: string = `CS|${generateUniqueIdentifier()}`;
let portWithBw: chrome.runtime.Port | null = null;

// Initialize port connection with error handling
try {
    portWithBw = chrome.runtime.connect(chrome.runtime.id, { name: uniquePortName });
    createPortListeners();
} catch (error) {
    console.error('KeriAuthCs: Failed to create initial port connection:', error);
    // Will attempt to reconnect when first message needs to be sent
}

// Observe and log URL changes in any SPA page. May be helpful for debugging potential issues.
window.addEventListener('popstate', (event) => {
    console.info(`KeriAuthCs ${event.type} ${window.location.href}`);
});

// Add listener for when DOMContentLoaded. Logging if helpful for debugging issues.
document.addEventListener('DOMContentLoaded', (event) => {
    console.info(`KeriAuthCs ${event.type}`);
});

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
 * Handle messages received from the BackgroundWorker
 * Routes responses back to the web page with appropriate formatting
 * @param message Message from the BackgroundWorker following polaris-web protocol
 */
function handleMsgFromBW(message: PW.MessageData<unknown>): void {
    if (!message.type) {
        console.error('KeriAuthCs←BW: type not found in message:', message);
        return;
    }

    console.groupCollapsed(`KeriAuthCs←BW: ${message.type}`);
    console.log(message);
    switch (message.type) {
        case BwCsMsgEnum.READY:
            // In the case the user has just clicked on Action Button and provided CS inject permission for first time,
            // assure the page is notified the extension is ready.
            postMessageToPageSignifyExtension();
            break;

        case BwCsMsgEnum.REPLY:
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

        case BwCsMsgEnum.REPLY_CANCELED:
            {
                const canceledMsg: ICsPageMsgData<null> = {
                    source: CsPageMsgTag,
                    type: BwCsMsgEnum.REPLY,
                    requestId: message.requestId,
                    error: message.error
                };
                postMessageToPage<ICsPageMsgData<null>>(canceledMsg);
            };
            break;

        case BwCsMsgEnum.APP_CLOSED:
            if (message.requestId) {
                const appClosedMsg: ICsPageMsgData<null> = {
                    source: CsPageMsgTag,
                    type: BwCsMsgEnum.REPLY,
                    requestId: message.requestId, // might be null
                    error: message.error
                };
                postMessageToPage<ICsPageMsgData<null>>(appClosedMsg);
            }
            break;

        default:
            console.warn(`KeriAuthCs unrecognized message type ${message.type}`);
            break;
    }
    console.groupEnd();
}

/**
 * Initialize the port connection with the BackgroundWorker
 * Sets up message and disconnect listeners, sends initial INIT message
 * Assumes portWithBw has already been created via chrome.runtime.connect()
 */
function createPortListeners(): void {
    console.log('KeriAuthCs→BW: creating port listeners');

    if (!portWithBw) {
        console.error('KeriAuthCs→BW: Port is null, cannot setup listeners');
        return;
    }

    console.log('KeriAuthCs→BW connected port:', portWithBw);

    // register to receive and handle messages from the extension (and indirectly also from the web page)
    portWithBw.onMessage.addListener((message: PW.MessageData<unknown>) => handleMsgFromBW(message));

    portWithBw.onDisconnect.addListener(() => {
        // disconnect will typically happen when the BackgroundWorker becomes inactive
        console.info('KeriAuthCs Port with BackgroundWorker was disconnected, likely due to BackgroundWorker going inactive.');
        // Set to null so assurePortAndSend knows to reconnect
        portWithBw = null;
    });

    // Send a ping message to the BackgroundWorker to help complete the setup of connection port
    const initMsg: ICsBwMsg = { type: CsBwMsgEnum.INIT };
    console.log('KeriAuthCs→BW Init:', initMsg);
    try {
        portWithBw.postMessage(initMsg);
        // Even though the Page hasn't requested the extension id via signify-extension-client here,
        // we send it anyway in case of when the script has been injected after user clicked the action button?
        // The following is not picked up by the page (that leverages polaris-web) if it has not requested it. So, user needs to reload the page.
        // postMessageToPageSignifyExtension();
        // TODO P2 Consider a reload message when permission is first granted and CS injected, to help the page pick up the extension id without user reload, e.g.:
        /* const ok = window.confirm("The extension needs to reload this page to finish setup. Reload now?");
            if (ok) {
                // Avoid touching DOM; just trigger a navigation:
                window.location.reload(); // or message BW to chrome.tabs.reload
            }
        */
    } catch (error) {
        console.error('KeriAuthCs→BW: Failed to send INIT message:', error);
        // Port may have disconnected already, set to null for reconnection
        portWithBw = null;
    }
    return;
}

/**
 * Ensure port connection exists and send message to BackgroundWorker
 * Automatically reconnects if port was disconnected (e.g., when BackgroundWorker went inactive)
 * @param msg Message to send, either polaris-web protocol or internal CS-BW message
 * @throws Error if unable to connect or send message
 */
function assurePortAndSend(msg: PW.MessageData<unknown> | ICsBwMsg): void {
    // Check if port is null (happens when BackgroundWorker goes inactive and disconnects)
    // Chrome extension ports cannot be reused after disconnection - must create new connection
    if (portWithBw === null) {
        console.info('KeriAuthCs re-creating port connection to BackgroundWorker');
        try {
            portWithBw = chrome.runtime.connect(chrome.runtime.id, { name: uniquePortName });
            createPortListeners();
        } catch (error) {
            console.error('KeriAuthCs→BW: Failed to reconnect to BackgroundWorker:', error);
            throw error; // Re-throw to let caller handle the error
        }
    }
    console.info('KeriAuthCs→BW: postMessage:', msg);
    try {
        portWithBw.postMessage(msg);
        // TODO P1 consider adding a timeout mechanism to detect if message was not delivered, and if READY is received within expected time.
    } catch (error) {
        console.error('KeriAuthCs→BW: Failed to send message:', error);
        // Port may have disconnected, set to null and re-throw
        portWithBw = null;
        throw error;
    }
}
function postMessageToPageSignifyExtension(): void {
    const extensionClientMsg: ICsPageMsgDataData<{ extensionId: string }> = {
        source: CsPageMsgTag,
        type: CsBwMsgEnum.POLARIS_SIGNIFY_EXTENSION,
        data: { extensionId: chrome.runtime.id },
        requestId: '' // may be unsolicited message or with no requestId, so no requestId set in this response
    };
    postMessageToPage<ICsPageMsgDataData<{ extensionId: string }>>(extensionClientMsg);
}

/**
 * Handle messages from the web page and route them to the BackgroundWorker
 * Validates message origin and filters out echo messages from content script
 * @param event Message event from the web page containing polaris-web protocol messages
 */
function handleWindowMessage(event: MessageEvent<IPageMessageData>): void {

    // Ignore messages with undefined events or .data, such as those sent from pages with advertising
    if (event === undefined || event.data === undefined) {
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
            case CsBwMsgEnum.POLARIS_SIGNIFY_EXTENSION_CLIENT: {
                postMessageToPageSignifyExtension();
                break;
            }
            case CsBwMsgEnum.POLARIS_GET_SESSION_INFO: {
                // const authorizeArgsMessage2 = event.data as PW.MessageData<PW.AuthorizeArgs>;
                // const authorizeResult: PW.AuthorizeResult = {};
                // TODO P2 implement sessions?
                const sessionInfoMsg: ICsPageMsgData<null> = {
                    source: CsPageMsgTag,
                    type: BwCsMsgEnum.REPLY,
                    requestId,
                    error: 'KERIAuthCs: sessions not supported'
                };
                postMessageToPage<ICsPageMsgData<null>>(sessionInfoMsg);
                break;
            }
            case CsBwMsgEnum.POLARIS_CONFIGURE_VENDOR: {
                const configureVendorArgsMessage = event.data.payload as PW.MessageData<PW.ConfigureVendorArgs>;
                console.info(`KeriAuthCs ${event.data.type} not implemented`, configureVendorArgsMessage);
                const configVendorMsg: ICsPageMsgData<null> = {
                    source: CsPageMsgTag,
                    type: BwCsMsgEnum.REPLY,
                    requestId,
                    error: 'KERIAuthCs: configure-vendor not supported'
                };
                postMessageToPage<ICsPageMsgData<null>>(configVendorMsg);
                break;
            }
            case CsBwMsgEnum.POLARIS_SIGNIFY_AUTHORIZE:
            case CsBwMsgEnum.POLARIS_SELECT_AUTHORIZE_CREDENTIAL:
            case CsBwMsgEnum.POLARIS_SELECT_AUTHORIZE_AID:
            case CsBwMsgEnum.POLARIS_SIGN_REQUEST:
                try {
                    console.info(`KeriAuthCs ${event.data.type}:`, event.data);

                    // Log headers for POLARIS_SIGN_REQUEST
                    if (event.data.type === CsBwMsgEnum.POLARIS_SIGN_REQUEST) {
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
                    console.error('KeriAuthCs→BW: error sending message {event.data} {e}:', event.data, error);
                    return;
                }
                break;

            case CsBwMsgEnum.POLARIS_CLEAR_SESSION: {
                // const authorizeArgsMessage3 = event.data as PW.MessageData<PW.AuthorizeArgs>;
                // Although sessions are not implemented, we can respond as expected when Clear is requested
                const clearResult: ICsPageMsgData<PW.AuthorizeResult> = {
                    source: CsPageMsgTag,
                    type: BwCsMsgEnum.REPLY, // type: "tab",
                    requestId,
                    payload: undefined
                };
                postMessageToPage<ICsPageMsgData<PW.AuthorizeResult>>(clearResult);
                break;
            }
            case CsBwMsgEnum.POLARIS_CREATE_DATA_ATTESTATION: {
                const createDataAttestationMessage = event.data as PW.MessageData<PW.CreateCredentialArgs>;
                console.info(`KeriAuthCs handler not implemented for ${event.data.type}`, { createDataAttestationMessage });
                break;
            }
            case CsBwMsgEnum.POLARIS_GET_CREDENTIAL:
                console.info(`KeriAuthCs handler not implemented for ${event.data.type}`, event.data);
                break;

            case CsBwMsgEnum.POLARIS_SIGN_DATA: {
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
