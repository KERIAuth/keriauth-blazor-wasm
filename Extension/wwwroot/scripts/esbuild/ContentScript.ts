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

// Add a listener for messages from the web page
window.addEventListener('message', (event: MessageEvent<IPageMessageData>) => handleWindowMessage(event));

// Listen for messages from BackgroundWorker (responses to our sendMessage calls)
chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
    // Respond to ping messages from the service worker to detect if content script is already injected
    if (msg?.type === 'ping') {
        (sendResponse as (response?: unknown) => void)({ ok: true });
        return true; // keep channel open if needed
    }

    // Handle messages from BackgroundWorker
    if (msg && typeof msg === 'object' && 'type' in msg) {
        console.log('KeriAuthCs←BW (via onMessage):', msg);
        handleMsgFromBW(msg as PW.MessageData<unknown>);
    } else {
        console.log('KeriAuthCs←BW unhandled (via onMessage):', msg, sender, sendResponse.toString());
        // Return false for other messages to allow other listeners to handle them
        return false;
    }


});

// Observe and log URL changes in any SPA page. May be helpful for debugging potential issues.
window.addEventListener('popstate', (event) => {
    console.info(`KeriAuthCs ${event.type} ${window.location.href}`);
});

// Add listener for when DOMContentLoaded. Logging if helpful for debugging issues.
document.addEventListener('DOMContentLoaded', (event) => {
    console.info(`KeriAuthCs ${event.type}`);
});


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

    console.log(`KeriAuthCs←BW: ${message.type}`);
    console.log(message);
    switch (message.type) {
        case BwCsMsgEnum.READY:
            // In the case the user has just clicked on Action Button and provided CS inject permission for first time,
            // assure the page is notified the extension is ready.
            postMessageToPageSignifyExtension();
            break;

        case BwCsMsgEnum.REPLY:
            console.log('KeriAuthCs←BW: reply:', message);
            if (message.error) {
                const errorMsg: ICsPageMsgData<null> = {
                    source: CsPageMsgTag,
                    type: message.type,
                    requestId: message.requestId,
                    error: String(message.error)
                };
                postMessageToPage<ICsPageMsgData<null>>(errorMsg);
            } else {
                // Check if payload contains credentialJson string that needs to be parsed
                let payload = message.payload as any;
                if (payload && payload.credentialJson && typeof payload.credentialJson === 'string') {
                    // Parse the credentialJson string to get the actual credential object
                    try {
                        payload = {
                            ...payload,
                            credential: JSON.parse(payload.credentialJson)
                        };
                        delete payload.credentialJson;
                    } catch (parseError) {
                        console.error('KeriAuthCs: Failed to parse credentialJson', parseError);
                    }
                }

                const msg: ICsPageMsgData<PW.AuthorizeResult> = {
                    source: CsPageMsgTag,
                    type: message.type,
                    requestId: message.requestId,
                    payload: payload as PW.AuthorizeResult
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
}

/**
 * Send message to BackgroundWorker using runtime.sendMessage
 * @param msg Message to send, either polaris-web protocol or internal CS-BW message
 */
async function sendMessageToBW(msg: PW.MessageData<unknown> | ICsBwMsg): Promise<void> {
    console.info('KeriAuthCs→BW: sendMessage:', msg);
    try {
        const response = await chrome.runtime.sendMessage(msg);
        // console.log('KeriAuthCs→BW: sendMessage:', msg);
        // Response handling can be added here if needed
    } catch (error) {
        // In that case, a more user-friendly message would be better here to prompt user to reload (or close tab, even) page.
        if (error as String === "Extension context invalidated.") {
            console.warn("KeriAuthCs→BW: Target context (e.g., service worker or tab) no longer active, perhaps due to an version update. Please reload the page.");
        } else {
            console.error('KeriAuthCs→BW: Failed to send message:', error);
        }
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
async function handleWindowMessage(event: MessageEvent<IPageMessageData>): Promise<void> {

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
    console.log(`KeriAuthCs←Page: ${event.data.type}`);
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
                    const signRequestMessage = event.data as PW.MessageData<PW.SignRequestArgs>;
                    if (event.data.type === CsBwMsgEnum.POLARIS_SIGN_REQUEST) {
                        if (signRequestMessage.payload?.headers) {
                            console.log('KeriAuthCs payload headers: ');
                            const headers = signRequestMessage.payload.headers;
                            for (const [key, value] of Object.keys(headers)) {
                                console.log(` ${key}: ${value}`);
                            }
                        }
                    }
                    await sendMessageToBW(signRequestMessage);
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
                // In this case, createDataAttestationMessage payload has shape of:
                // { credData: { digest: "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", digestAlgo: "SHA-256"}, schemaSaid: "ENDcMNUZjag27T_GTxiCmB2kYstg_kqipqz39906E_FD" }
                await sendMessageToBW(createDataAttestationMessage);
                break;
            }
            case CsBwMsgEnum.POLARIS_GET_CREDENTIAL:
                console.info(`KeriAuthCs handler not implemented for ${event.data.type}`, event.data);
                break;

            case CsBwMsgEnum.POLARIS_SIGN_DATA: {
                const signDataArgsMsg = event.data as PW.MessageData<PW.SignDataArgs>;
                await sendMessageToBW(signDataArgsMsg);
                // console.info(`KeriAuthCs handler not implemented for ${signDataArgsMsg.type}`, signDataArgsMsg);
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
};
