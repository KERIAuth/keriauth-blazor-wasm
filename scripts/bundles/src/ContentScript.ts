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
    type ICsReadyMessage,
    CsPageMsgTag,
    CsBwMsgEnum,
    CsInternalMsgEnum,
    BwCsMsgEnum,
    type Polaris,
    type PortMessage,
    type ReadyMessage,
    type RpcResponse,
    type EventMessage,
    createCsHelloMessage,
    createRpcRequest,
    isReadyMessage,
    isRpcResponse,
    isEventMessage
} from '@keriauth/types';

// CONTEXT GUARD: ContentScript should only run in web page context, not in service worker.
// BackgroundWorker.js imports all scripts in wwwroot/scripts/, so we must bail out early
// if we detect we're in a service worker context.
// Note: browser-polyfill.js creates window/document proxies in service worker, so we can't
// just check for their existence. Instead, check for ServiceWorkerGlobalScope on globalThis.
// Wrapping all executable code in an IIFE that checks context first.
(function initContentScript() {
    // Check if we're in a service worker context
    // ServiceWorkerGlobalScope only exists in service workers, not in web page contexts
    if ('ServiceWorkerGlobalScope' in globalThis) {
        // We're in a service worker - do not initialize
        // This is expected when BackgroundWorker.js imports this file
        console.log('KeriAuthCs Running in ServiceWorker context, skipping initialization');
        return;
    }

    // Type for messages from the page (combines polaris-web messages and CS-specific messages)
    // Some messages may have a 'source' property to identify origin
    type IPageMessageData =
        (Polaris.MessageData<Polaris.AuthorizeArgs> & { source?: string })
        | (Polaris.MessageData<Polaris.CreateCredentialArgs> & { source?: string })
        | (Polaris.MessageData<Polaris.SignDataArgs> & { source?: string })
        | (Polaris.MessageData<Polaris.SignRequestArgs> & { source?: string })
        | (Polaris.MessageData<Polaris.ConfigureVendorArgs> & { source?: string })
        | (Polaris.MessageData<null> & { source?: string })
        | ICsPageMsgData<unknown>;

    // Extended message type that includes both 'data' (legacy BW format) and 'payload' (Polaris spec)
    type BwMessage = Polaris.MessageData<unknown> & { data?: unknown };

    // Port-based messaging state
    let port: chrome.runtime.Port | null = null;
    let portSessionId: string | null = null;
    let isPortReady = false;
    const instanceId = crypto.randomUUID();
    const messageQueue: Array<{ method: string; params?: unknown }> = [];
    const pendingRpcCallbacks = new Map<string, {
        resolve: (value: unknown) => void;
        reject: (reason: unknown) => void;
    }>();
    // Map RPC request ID -> original page requestId for response routing
    const rpcIdToPageRequestId = new Map<string, string>();
    let reconnectAttempts = 0;
    const MAX_RECONNECT_ATTEMPTS = 5;
    const RECONNECT_DELAY_MS = 100;

    /*
     * This section is evaluated on document-start, as specified in the extension manifest.
     * Because of the timing, you might not see some of these log messages.
     */

    // Sentinel pattern to prevent double-injection
    const KEY = '__KERIAUTH_CS_INJECTED__';
    if ((globalThis as unknown as Record<string, boolean>)[KEY]) {
        console.log('KeriAuthCs: Already injected, skipping initialization');
        return; // already active, do nothing
    }
    (globalThis as unknown as Record<string, boolean>)[KEY] = true;

    console.log('KeriAuthCs: initializing');
    const currentOrigin = window.location.origin;
    console.log('KeriAuthCs: currentOrigin:', currentOrigin);
    console.log('KeriAuthCs: ', chrome.runtime.getManifest().name, chrome.runtime.getManifest().version_name, chrome.runtime.id);

    // Add a listener for messages from the web page
    window.addEventListener('message', (event: MessageEvent<IPageMessageData>) => handleWindowMessage(event));

    // Listen for messages from BackgroundWorker (ping for icon state, SW_RESTARTED for reconnection)
    // NOTE: All CS↔BW messaging now uses port-based communication. This listener is ONLY for:
    // 1. ping - to detect if content script is injected (handled locally, no BW roundtrip)
    // 2. SW_RESTARTED - to trigger port reconnection after service worker restart
    chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
        // Respond to ping messages from the service worker to detect if content script is already injected
        if (msg?.type === 'ping') {
            (sendResponse as (response?: unknown) => void)({ ok: true });
            return true; // keep channel open for response
        }

        // Handle SW_RESTARTED message - reconnect port after service worker restart
        if (msg?.type === 'SW_RESTARTED') {
            console.log('KeriAuthCs: Received SW_RESTARTED, reconnecting port...');
            // Reset state and reconnect
            port = null;
            portSessionId = null;
            isPortReady = false;
            reconnectAttempts = 0;
            connectPort();
            return false;
        }

        // Ignore all other messages - all CS↔BW communication uses ports now
        return false;
    });

    // Observe and log URL changes in any SPA page. May be helpful for debugging potential issues.
    window.addEventListener('popstate', (event) => {
        console.info(`KeriAuthCs ${event.type} ${window.location.href}`);
    });

    // Add listener for when DOMContentLoaded. Logging if helpful for debugging issues.
    document.addEventListener('DOMContentLoaded', (event) => {
        console.info(`KeriAuthCs ${event.type}`);
    });

    // Connect to BackgroundWorker using port-based messaging
    connectPort();

    /**
     * Establish a port connection to the BackgroundWorker
     * Sends HELLO message and waits for READY response
     */
    function connectPort(): void {
        try {
            console.log('KeriAuthCs: Connecting to BackgroundWorker via port...');

            // Create port connection
            port = chrome.runtime.connect(undefined, {name: 'content-script'});

            // Set up message listener
            port.onMessage.addListener(handlePortMessage);

            // Set up disconnect listener
            port.onDisconnect.addListener(handlePortDisconnect);

            // Send HELLO message
            const helloMessage = createCsHelloMessage(instanceId);
            port.postMessage(helloMessage);

            console.log('KeriAuthCs: HELLO sent to BackgroundWorker', { instanceId });
        } catch (error) {
            console.error('KeriAuthCs: Failed to connect port:', error);
            scheduleReconnect();
        }
    }

    /**
     * Handle messages received from BackgroundWorker via port
     */
    function handlePortMessage(message: PortMessage): void {
        console.log('KeriAuthCs←BW (port):', message);

        if (isReadyMessage(message)) {
            handleReadyMessage(message);
        } else if (isRpcResponse(message)) {
            handleRpcResponse(message);
        } else if (isEventMessage(message)) {
            handleEventMessage(message);
        } else {
            console.warn('KeriAuthCs: Unknown port message type:', message);
        }
    }

    /**
     * Handle READY message from BackgroundWorker
     */
    function handleReadyMessage(message: ReadyMessage): void {
        portSessionId = message.portSessionId;
        isPortReady = true;
        reconnectAttempts = 0; // Reset on successful connection

        console.log('KeriAuthCs: Port ready, portSessionId:', portSessionId);

        // Process any queued messages
        while (messageQueue.length > 0) {
            const queued = messageQueue.shift()!;
            sendRpcRequest(queued.method, queued.params);
        }

        // Notify the page that the extension is ready
        postMessageToPageSignifyExtension();
    }

    /**
     * Handle RPC_RES messages from BackgroundWorker
     */
    function handleRpcResponse(message: RpcResponse): void {
        const callback = pendingRpcCallbacks.get(message.id);

        // Check if this is a response to a page→BW message (has original requestId mapping)
        const originalRequestId = rpcIdToPageRequestId.get(message.id);
        if (originalRequestId) {
            // This is a response to a page request - route to page via handleMsgFromBW
            rpcIdToPageRequestId.delete(message.id);
            pendingRpcCallbacks.delete(message.id); // Clean up callback if any

            const legacyMessage = {
                type: message.error ? BwCsMsgEnum.REPLY : BwCsMsgEnum.REPLY,
                requestId: originalRequestId, // Use original page requestId
                data: message.result,
                error: message.error
            };
            handleMsgFromBW(legacyMessage as BwMessage);
            return;
        }

        if (callback) {
            pendingRpcCallbacks.delete(message.id);
            if (message.error) {
                callback.reject(new Error(message.error));
            } else {
                callback.resolve(message.result);
            }
        } else {
            // Route as a regular BW message for backward compatibility
            const legacyMessage = {
                type: message.error ? BwCsMsgEnum.REPLY : BwCsMsgEnum.REPLY,
                requestId: message.id,
                data: message.result,
                error: message.error
            };
            handleMsgFromBW(legacyMessage as BwMessage);
        }
    }

    /**
     * Handle EVENT messages from BackgroundWorker
     */
    function handleEventMessage(message: EventMessage): void {
        // Convert to legacy format and route through existing handler
        const legacyMessage = {
            type: message.name,
            requestId: '', // EventMessage doesn't have a requestId
            data: message.data
        };
        handleMsgFromBW(legacyMessage as BwMessage);
    }

    /**
     * Handle port disconnect
     */
    function handlePortDisconnect(): void {
        // chrome.runtime.lastError exists at runtime but may not be in type definitions
        const error = (chrome.runtime as { lastError?: { message?: string } }).lastError;
        console.log('KeriAuthCs: Port disconnected', error?.message || '');

        port = null;
        portSessionId = null;
        isPortReady = false;

        // Check if extension was invalidated
        if (error?.message?.includes('Extension context invalidated')) {
            console.log('KeriAuthCs: Extension context invalidated - prompting reload');
            promptAndReloadPage(
                "The KERI Auth extension has been updated or reloaded.\n" +
                "Actions needed:\n" +
                "1) Click OK to reload this page. In some cases, you may need to close the tab.\n\n" +
                "2) If the extension action button is not visible, click the puzzle piece icon in the browser toolbar and pin the KERI Auth extension for easier access.\n\n" +
                "3) Click the KERI Auth extension action button to re-authorize this site."
            );
            return;
        }

        // Attempt reconnection for other disconnects (e.g., service worker restart)
        scheduleReconnect();
    }

    /**
     * Schedule a reconnection attempt with exponential backoff
     */
    function scheduleReconnect(): void {
        if (reconnectAttempts >= MAX_RECONNECT_ATTEMPTS) {
            console.warn('KeriAuthCs: Max reconnect attempts reached');
            return;
        }

        reconnectAttempts++;
        const delay = RECONNECT_DELAY_MS * Math.pow(2, reconnectAttempts - 1);
        console.log(`KeriAuthCs: Scheduling reconnect attempt ${reconnectAttempts} in ${delay}ms`);

        setTimeout(() => {
            connectPort();
        }, delay);
    }

    /**
     * Send an RPC request via port
     */
    function sendRpcRequest(method: string, params?: unknown): Promise<unknown> {
        return new Promise((resolve, reject) => {
            if (!isPortReady || !port || !portSessionId) {
                // Queue the message if not ready
                console.log('KeriAuthCs: Port not ready, queueing message:', method);
                messageQueue.push({ method, params });
                // Don't reject - the message will be sent when port is ready
                // For now, resolve with undefined (caller should handle async response)
                resolve(undefined);
                return;
            }

            const request = createRpcRequest(portSessionId, method, params);

            // Store callback for response
            pendingRpcCallbacks.set(request.id, { resolve, reject });

            // Send via port
            port.postMessage(request);
            console.log('KeriAuthCs→BW (port):', request);
        });
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
    function handleMsgFromBW(message: BwMessage): void {
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
                    // Check if data contains credentialJson string that needs to be parsed
                    // BW may send 'data' (legacy) or 'payload' (Polaris spec)
                    let data = (message.data ?? message.payload) as any;
                    if (data && data.credentialJson && typeof data.credentialJson === 'string') {
                        // Parse the credentialJson string to get the actual credential object
                        try {
                            data = {
                                ...data,
                                credential: JSON.parse(data.credentialJson)
                            };
                            delete data.credentialJson;
                        } catch (parseError) {
                            console.error('KeriAuthCs: Failed to parse credentialJson', parseError);
                        }
                    }

                    const msg: ICsPageMsgData<Polaris.AuthorizeResult> = {
                        source: CsPageMsgTag,
                        type: message.type,
                        requestId: message.requestId,
                        payload: data as Polaris.AuthorizeResult
                    };
                    postMessageToPage<ICsPageMsgData<Polaris.AuthorizeResult>>(msg);
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
                console.info(`KeriAuthCs unrecognized message type ${message.type}`);
                break;
        }
    }

    /**
     * Prompts user to reload the page
     * @param message The confirmation message to display
     * @returns true if user accepted and reload was triggered, false otherwise
     */
    function promptAndReloadPage(message: string): boolean {
        try {
            const userAccepted = confirm(message);
            if (userAccepted) {
                console.log('KeriAuthCs: User accepted reload prompt - reloading page');
                window.location.reload();
                return true;
            } else {
                console.log('KeriAuthCs: User declined reload prompt');
                return false;
            }
        } catch (error) {
            console.error('KeriAuthCs: ERROR in promptAndReloadPage:', error);
            return false;
        }
    }

    /**
     * Send message to BackgroundWorker using port-based messaging.
     * Throws an error if port is not connected - no sendMessage fallback.
     * @param msg Message to send, either polaris-web protocol or internal CS-BW message
     */
    async function sendMessageToBW(msg: Polaris.MessageData<unknown> | ICsBwMsg): Promise<void> {
        console.info('KeriAuthCs→BW:', msg);

        // Port must be ready - no fallback to sendMessage
        if (!isPortReady || !port || !portSessionId) {
            console.error('KeriAuthCs→BW: Port not connected, cannot send message');
            throw new Error('Port not connected to BackgroundWorker');
        }

        // Ensure we have a requestId for response routing
        const originalRequestId = msg.requestId || crypto.randomUUID();

        // Send as RPC request via port
        const method = msg.type;
        const params = {
            ...msg,
            requestId: originalRequestId
        };

        try {
            // Send the RPC request and get the RPC ID
            const rpcId = sendRpcRequestForPage(method, params, originalRequestId);
            console.log('KeriAuthCs→BW: RPC sent, rpcId=', rpcId, 'originalRequestId=', originalRequestId);
        } catch (error) {
            console.error('KeriAuthCs→BW: Port send failed:', error);
            throw error;
        }
    }

    /**
     * Send an RPC request for page→BW messages.
     * Stores mapping from RPC ID → original page requestId for response routing.
     * Returns the RPC ID (not a promise for the response - response is routed to page).
     */
    function sendRpcRequestForPage(method: string, params: unknown, originalRequestId: string): string {
        if (!isPortReady || !port || !portSessionId) {
            throw new Error('Port not connected');
        }

        const request = createRpcRequest(portSessionId, method, params);

        // Store mapping for response routing back to page
        rpcIdToPageRequestId.set(request.id, originalRequestId);

        // Send via port (no callback - response routed via handleRpcResponse)
        port.postMessage(request);
        console.log('KeriAuthCs→BW (port):', request);

        return request.id;
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
        console.log(`KeriAuthCs←Page: type: ${event.data.type} data: ${event.data}`);
        try {
            const requestId = event.data.requestId;
            switch (event.data.type) {
                case CsBwMsgEnum.POLARIS_SIGNIFY_EXTENSION_CLIENT: {
                    postMessageToPageSignifyExtension();
                    break;
                }
                case CsBwMsgEnum.POLARIS_GET_SESSION_INFO: {
                    // const authorizeArgsMessage2 = event.data as Polaris.MessageData<Polaris.AuthorizeArgs>;
                    // const authorizeResult: Polaris.AuthorizeResult = {};
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
                    const configureVendorArgsMessage = event.data.payload as Polaris.MessageData<Polaris.ConfigureVendorArgs>;
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
                        const signRequestMessage = event.data as Polaris.MessageData<Polaris.SignRequestArgs>;
                        if (event.data.type === CsBwMsgEnum.POLARIS_SIGN_REQUEST) {
                            console.log('KeriAuthCs: SIGN_REQUEST payload:', JSON.stringify(signRequestMessage.payload));
                            if (signRequestMessage.payload?.headers) {
                                console.log('KeriAuthCs: SIGN_REQUEST headers ', JSON.stringify(signRequestMessage.payload.headers));
                                const headers = signRequestMessage.payload.headers;
                                for (const key of Object.keys(headers)) {
                                    console.log(`  KeriAuthCs header: ${key} = ${headers[key]}`);
                                }
                            } else {
                                console.log('KeriAuthCs: SIGN_REQUEST: no headers in payload');
                            }
                        }
                        await sendMessageToBW(signRequestMessage);
                    } catch (error) {
                        console.error('KeriAuthCs→BW: error sending message {event.data} {e}:', event.data, error);
                        return;
                    }
                    break;

                case CsBwMsgEnum.POLARIS_CLEAR_SESSION: {
                    // const authorizeArgsMessage3 = event.data as Polaris.MessageData<Polaris.AuthorizeArgs>;
                    // Although sessions are not implemented, we can respond as expected when Clear is requested
                    const clearResult: ICsPageMsgData<Polaris.AuthorizeResult> = {
                        source: CsPageMsgTag,
                        type: BwCsMsgEnum.REPLY, // type: "tab",
                        requestId,
                        payload: undefined
                    };
                    postMessageToPage<ICsPageMsgData<Polaris.AuthorizeResult>>(clearResult);
                    break;
                }
                case CsBwMsgEnum.POLARIS_CREATE_DATA_ATTESTATION: {
                    const createDataAttestationMessage = event.data as Polaris.MessageData<Polaris.CreateCredentialArgs>;
                    // In this case, createDataAttestationMessage payload has shape of:
                    // { credData: { digest: "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", digestAlgo: "SHA-256"}, schemaSaid: "ENDcMNUZjag27T_GTxiCmB2kYstg_kqipqz39906E_FD" }
                    await sendMessageToBW(createDataAttestationMessage);
                    break;
                }
                case CsBwMsgEnum.POLARIS_GET_CREDENTIAL:
                    console.info(`KeriAuthCs handler not implemented for ${event.data.type}`, event.data);
                    break;

                case CsBwMsgEnum.POLARIS_SIGN_DATA: {
                    const signDataArgsMsg = event.data as Polaris.MessageData<Polaris.SignDataArgs>;
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
    }
})();
