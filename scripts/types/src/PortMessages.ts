/**
 * Port-based messaging types for the KERI Auth browser extension.
 * These types match the C# records in Extension/Models/Messages/Port/PortMessages.cs
 */

/**
 * Context types for port connections.
 */
export type ContextKind = 'content-script' | 'extension-app';

/**
 * Port names used for chrome.runtime.connect().
 */
export const PortNames = {
    ContentScript: 'cs',
    ExtensionApp: 'app'
} as const;

/**
 * Message type discriminators.
 */
export const PortMessageTypes = {
    Hello: 'HELLO',
    Ready: 'READY',
    AttachTab: 'ATTACH_TAB',
    DetachTab: 'DETACH_TAB',
    Event: 'EVENT',
    RpcRequest: 'RPC_REQ',
    RpcResponse: 'RPC_RES',
    Error: 'ERROR'
} as const;

/**
 * Known error codes for ErrorMessage.
 */
export const PortErrorCodes = {
    AttachFailed: 'ATTACH_FAILED',
    InvalidMessage: 'INVALID_MESSAGE',
    SessionNotFound: 'SESSION_NOT_FOUND'
} as const;

/**
 * HELLO message sent by CS or App immediately after connecting.
 */
export interface HelloMessage {
    t: typeof PortMessageTypes.Hello;
    context: ContextKind;
    instanceId: string;
    tabId?: number;
    frameId?: number;
}

/**
 * READY message sent by BW in response to HELLO.
 */
export interface ReadyMessage {
    t: typeof PortMessageTypes.Ready;
    portSessionId: string;
    tabId?: number;
    frameId?: number;
}

/**
 * ATTACH_TAB message sent by App to associate with a tab's PortSession.
 */
export interface AttachTabMessage {
    t: typeof PortMessageTypes.AttachTab;
    tabId: number;
    frameId?: number;
}

/**
 * DETACH_TAB message sent by App to detach from its PortSession.
 */
export interface DetachTabMessage {
    t: typeof PortMessageTypes.DetachTab;
}

/**
 * EVENT message for one-way notifications.
 */
export interface EventMessage {
    t: typeof PortMessageTypes.Event;
    portSessionId: string;
    name: string;
    data?: unknown;
}

/**
 * RPC_REQ message for request-response patterns.
 */
export interface RpcRequest {
    t: typeof PortMessageTypes.RpcRequest;
    portSessionId: string;
    id: string;
    method: string;
    params?: unknown;
}

/**
 * RPC_RES message sent in reply to an RpcRequest.
 */
export interface RpcResponse {
    t: typeof PortMessageTypes.RpcResponse;
    portSessionId: string;
    id: string;
    ok: boolean;
    result?: unknown;
    error?: string;
}

/**
 * ERROR message sent when an operation fails.
 */
export interface ErrorMessage {
    t: typeof PortMessageTypes.Error;
    code: string;
    message: string;
}

/**
 * Union type of all port message types.
 */
export type PortMessage =
    | HelloMessage
    | ReadyMessage
    | AttachTabMessage
    | DetachTabMessage
    | EventMessage
    | RpcRequest
    | RpcResponse
    | ErrorMessage;

/**
 * Type guard for HelloMessage.
 */
export function isHelloMessage(msg: PortMessage): msg is HelloMessage {
    return msg.t === PortMessageTypes.Hello;
}

/**
 * Type guard for ReadyMessage.
 */
export function isReadyMessage(msg: PortMessage): msg is ReadyMessage {
    return msg.t === PortMessageTypes.Ready;
}

/**
 * Type guard for RpcRequest.
 */
export function isRpcRequest(msg: PortMessage): msg is RpcRequest {
    return msg.t === PortMessageTypes.RpcRequest;
}

/**
 * Type guard for RpcResponse.
 */
export function isRpcResponse(msg: PortMessage): msg is RpcResponse {
    return msg.t === PortMessageTypes.RpcResponse;
}

/**
 * Type guard for EventMessage.
 */
export function isEventMessage(msg: PortMessage): msg is EventMessage {
    return msg.t === PortMessageTypes.Event;
}

/**
 * Type guard for ErrorMessage.
 */
export function isErrorMessage(msg: PortMessage): msg is ErrorMessage {
    return msg.t === PortMessageTypes.Error;
}

/**
 * Context kind values for use in code.
 */
export const ContextKinds = {
    ContentScript: 'content-script',
    ExtensionApp: 'extension-app'
} as const satisfies Record<string, ContextKind>;

/**
 * Helper to create a HelloMessage for ContentScript.
 */
export function createCsHelloMessage(instanceId: string): HelloMessage {
    return {
        t: PortMessageTypes.Hello,
        context: ContextKinds.ContentScript,
        instanceId
    };
}

/**
 * Helper to create a HelloMessage for ExtensionApp.
 */
export function createAppHelloMessage(instanceId: string): HelloMessage {
    return {
        t: PortMessageTypes.Hello,
        context: ContextKinds.ExtensionApp,
        instanceId
    };
}

/**
 * Helper to create an RpcRequest.
 */
export function createRpcRequest(
    portSessionId: string,
    method: string,
    params?: unknown,
    id?: string
): RpcRequest {
    return {
        t: PortMessageTypes.RpcRequest,
        portSessionId,
        id: id ?? crypto.randomUUID(),
        method,
        params
    };
}

/**
 * Helper to create a success RpcResponse.
 */
export function createRpcSuccessResponse(
    portSessionId: string,
    id: string,
    result?: unknown
): RpcResponse {
    return {
        t: PortMessageTypes.RpcResponse,
        portSessionId,
        id,
        ok: true,
        result
    };
}

/**
 * Helper to create an error RpcResponse.
 */
export function createRpcErrorResponse(
    portSessionId: string,
    id: string,
    error: string
): RpcResponse {
    return {
        t: PortMessageTypes.RpcResponse,
        portSessionId,
        id,
        ok: false,
        error
    };
}
