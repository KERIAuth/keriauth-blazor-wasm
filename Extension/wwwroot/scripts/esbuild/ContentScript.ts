/// <reference types="chrome-types" />

// This ContentScript is inserted into tabs after a user provided permission for the site (after having clicked on the extension action button)
// The purpose of the ContentScript is primarily to shuttle messages from a web page to/from the extension BackgroundWorker.

// windows message event data
// TODO P2 better imported form somewhere?
interface EventData {
    type: string;
    [key: string]: any;
}

import type {
    ICsPageMsgData,
    ICsPageMsgDataData,
    ICsSwMsg} from '../es6/ExCsInterfaces';

import {
    CsPageMsgTag,
    CsSwMsgEnum,
    SwCsMsgEnum
} from '../es6/ExCsInterfaces';

import type * as PW from '../types/polaris-web-client';

/*
 * This section is evaluated on document-start, as specified in the extension manifest
 */

// TODO P2 clarify naming consistency and use of: tab, page (including DOM and properties), document (high-level properties), and DOM

// Unique port name for communications between the content script and the extension
const currentOrigin = window.location.origin;
let uniquePortName: string;
let portWithSw: chrome.runtime.Port | null;

console.groupCollapsed('KeriAuthCs initializing');

// Add a listener for messages and create port with SW
window.addEventListener('message', (event: MessageEvent<EventData>) => handleWindowMessage(event));
createPortWithSw();

// Observe and log URL changes in any SPA page. May be helpful for debugging potential issues.
window.addEventListener('popstate', (event) => {
    console.info(`KeriAuthCs ${event.type} ${window.location.href}`);
});

// Add listener for when DOMContentLoaded. Log if helpful for debugging issues.
document.addEventListener('DOMContentLoaded', (_) => {
    // console.info(`KeriAuthCs ${event.type} ${window.location.href}`);
});
console.groupEnd();

/*
 * Generate a unique and unguessable identifier for the port name for communications between the content script and the extension
 */
function generateUniqueIdentifier(): string {
    const array = new Uint32Array(4);
    // TODO P4 consider randumUUID() instead
    window.crypto.getRandomValues(array);
    return Array.from(array, dec => (`00000000${  dec.toString(16)}`).slice(-8)).join('-');
}

/*
 *
 */
function postMessageToPage<T>(msg: T): void {
    console.log(`KeriAuthCs→Page: ${(msg as ICsPageMsgData<T>).type}`, { msg });
    window.postMessage(msg, currentOrigin);
}

/*
 * Handle messages from SW
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
            const canceledMsg: ICsPageMsgData<null> = {
                source: CsPageMsgTag,
                type: SwCsMsgEnum.REPLY,
                requestId: message.requestId,
                error: message.error
            };
            postMessageToPage<ICsPageMsgData<null>>(canceledMsg);
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

/*
 *
 */
function createPortWithSw(): void {
    console.groupCollapsed('KeriAuthCs→SW: creating port');
    uniquePortName = generateUniqueIdentifier();
    portWithSw = chrome.runtime.connect(uniquePortName);
    console.log('KeriAuthCs→SW connected port:', portWithSw);

    // register to receive and handle messages from the extension (and indirectly also from the web page)
    portWithSw.onMessage.addListener((message: PW.MessageData<unknown>) => handleMsgFromSW(message));

    portWithSw.onDisconnect.addListener((_) => {
        // disconnect will typically happen when the BackgroundWorker becomes inactive
        console.info('KeriAuthCs Port with BackgroundWorker was disconnected, likely due to BackgroundWorker going inactive.');
        portWithSw = null;
    });

    // Send a ping message to the BackgroundWorker to help complete the setup of connection port
    const initMsg: ICsSwMsg = { type: CsSwMsgEnum.INIT };
    console.log('KeriAuthCs→SW Init:', initMsg);
    portWithSw.postMessage(initMsg);
    console.groupEnd();
}

/*
 * Assure port with SW exists, then postMessage
 */
function assurePortAndSend(msg: any) {
    if (portWithSw === null) {
        console.info('KeriAuthCs re-createPortWithCs()');
        createPortWithSw();
    }
    console.info('KeriAuthCs→SW: postMessage:', msg);
    portWithSw!.postMessage(msg);
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
        return;
    }

    // Ignore messages from Cs sent to the Tab (instead of from Tab)
    if (event.data.source == CsPageMsgTag) {
        return;
    }

    // handle messages from current page
    console.groupCollapsed(`KeriAuthCs←Page: ${event.data.type}`);
    console.log(event.data);
    try {
        const requestId = (event.data as PW.MessageData<any>).requestId;
        switch (event.data.type) {
            case CsSwMsgEnum.POLARIS_SIGNIFY_EXTENSION_CLIENT:
                const extensionClientMsg: ICsPageMsgDataData<{ extensionId: string }> = {
                    source: CsPageMsgTag,
                    type: CsSwMsgEnum.POLARIS_SIGNIFY_EXTENSION,
                    data: { extensionId: chrome.runtime.id },
                    requestId
                };
                postMessageToPage<ICsPageMsgDataData<{ extensionId: string }>>(extensionClientMsg);
                break;

            case CsSwMsgEnum.POLARIS_SIGNIFY_EXTENSION:
                const extensionMessage: ICsPageMsgDataData<{ extensionId: string }> = {
                    source: CsPageMsgTag,
                    type: CsSwMsgEnum.POLARIS_SIGNIFY_EXTENSION,
                    data: { extensionId: chrome.runtime.id },
                    requestId
                };
                postMessageToPage<ICsPageMsgDataData<{ extensionId: string }>>(extensionMessage);
                break;

            case CsSwMsgEnum.POLARIS_GET_SESSION_INFO:
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

            case CsSwMsgEnum.POLARIS_CONFIGURE_VENDOR:
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

            case CsSwMsgEnum.POLARIS_SIGNIFY_AUTHORIZE:
            case CsSwMsgEnum.POLARIS_SELECT_AUTHORIZE_CREDENTIAL:
            case CsSwMsgEnum.POLARIS_SELECT_AUTHORIZE_AID:
            case CsSwMsgEnum.POLARIS_SIGN_REQUEST:
                try {
                    const authorizeRequestMessage = event.data as PW.MessageData<null>;
                    console.info(`KeriAuthCs ${authorizeRequestMessage.type}:`, authorizeRequestMessage);

                    // TODO P2 this could be simplified with the correct type above?
                    // only relevant for POLARIS_SIGN_REQUEST ?
                    if (event.data.payload?.headers) {
                        console.log('KeriAuthCs payload headers: ');
                        const hs: Headers = event.data.payload?.headers;
                        for (const pair of hs.entries()) {
                            console.log(` ${pair[0]}: ${pair[1]}`);
                        }
                    }
                    assurePortAndSend(event.data);
                } catch (error) {
                    console.error('KeriAuthCs→SW: error sending message {event.data} {e}:', event.data, error);
                    return;
                }
                break;

            case CsSwMsgEnum.POLARIS_CLEAR_SESSION:
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

            case CsSwMsgEnum.POLARIS_CREATE_DATA_ATTESTATION:
                const createDataAttestationMessage = event.data as PW.MessageData<PW.CreateCredentialArgs>;
                console.info(`KeriAuthCs handler not implemented for ${event.data.type}`, { createDataAttestationMessage });
                break;

            case CsSwMsgEnum.POLARIS_GET_CREDENTIAL:
                console.info(`KeriAuthCs handler not implemented for ${event.data.type}`, event.data);
                break;

            case CsSwMsgEnum.POLARIS_SIGN_DATA:
                const signDataArgsMsg = event.data as PW.MessageData<PW.SignDataArgs>;
                console.info(`KeriAuthCs handler not implemented for ${signDataArgsMsg.type}`, signDataArgsMsg);
                break;

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
