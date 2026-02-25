/**
 * RPC method constants for ContentScript → BackgroundWorker communication.
 * These match the C# CsBwMessageTypes in Extension/Models/Messages/CsBw/CsBwMessages.cs
 *
 * Naming convention:
 * - CsBw = ContentScript → BackgroundWorker direction
 * - Method names match polaris-web protocol where applicable
 */

/**
 * Port message type discriminators for CS→BW direction.
 * These are directional variants of the generic PortMessageTypes,
 * making it easier to identify message direction in logs.
 */
export const CsBwPortMessageTypes = {
    /** RPC request from ContentScript to BackgroundWorker */
    RpcRequest: 'CS_BW_RPC_REQ',
    /** RPC response from BackgroundWorker to ContentScript */
    RpcResponse: 'BW_CS_RPC_RES'
} as const;

/**
 * Union type of CS→BW port message discriminators.
 */
export type CsBwPortMessageType = typeof CsBwPortMessageTypes[keyof typeof CsBwPortMessageTypes];

/**
 * RPC method constants for CS→BW requests.
 * Use these as the `method` parameter in RpcRequest messages.
 */
export const CsBwRpcMethods = {
    /** Request user to authorize (select identifier or credential) */
    Authorize: '/signify/authorize',

    /** Request user to select an identifier (AID) only */
    SelectAuthorizeAid: '/signify/authorize/aid',

    /** Request user to select a credential only */
    SelectAuthorizeCredential: '/signify/authorize/credential',

    /** Request user to sign arbitrary data items */
    SignData: '/signify/sign-data',

    /** Request to sign an HTTP request (may auto-approve for safe methods) */
    SignRequest: '/signify/sign-request',

    /** Request to get current session info (not implemented) */
    GetSessionInfo: '/signify/get-session-info',

    /** Request to clear current session (not implemented) */
    ClearSession: '/signify/clear-session',

    /** Request to create a data attestation credential */
    CreateDataAttestation: '/signify/credential/create/data-attestation',

    /** Request to get a credential by SAID */
    GetCredential: '/signify/credential/get',

    /** Query if extension is installed (returns extension ID) */
    SignifyExtension: 'signify-extension',

    /** Query if extension client is available */
    SignifyExtensionClient: 'signify-extension-client',

    /** Configure vendor URL for theming */
    ConfigureVendor: '/signify/configure-vendor',

    /** Request mutual OOBI exchange to establish a connection */
    ConnectionInvite: '/KeriAuth/connection/invite',

    /** Confirm that the page resolved the reciprocal OOBI (fire-and-forget) */
    ConnectionConfirm: '/KeriAuth/connection/confirm',

    /** ContentScript initialization (legacy) */
    Init: 'init'
} as const;

/**
 * Union type of all valid CS→BW RPC method strings.
 */
export type CsBwRpcMethod = typeof CsBwRpcMethods[keyof typeof CsBwRpcMethods];

/**
 * Type guard to check if a string is a valid CS→BW RPC method.
 * @param method The method string to validate
 * @returns True if the method is a valid CsBwRpcMethod
 */
export function isCsBwRpcMethod(method: string): method is CsBwRpcMethod {
    return Object.values(CsBwRpcMethods).includes(method as CsBwRpcMethod);
}

/**
 * BW→CS response type constants.
 * These match the C# BwCsMessageTypes in Extension/Models/Messages/BwCs/BwCsMessages.cs
 *
 * Naming convention:
 * - BwCs = BackgroundWorker → ContentScript direction
 */
export const BwCsMessageTypes = {
    /** BackgroundWorker is ready */
    Ready: 'ready',

    /** Reply to a request (success or error) - matches polaris-web protocol */
    Reply: '/signify/reply',

    /** User canceled the operation */
    ReplyCanceled: 'reply_canceled',

    /** Reply with credential data */
    ReplyCredential: '/KeriAuth/signify/replyCredential',

    /** Generic message from BackgroundWorker */
    FromBackgroundWorker: 'fromBackgroundWorker',

    /** App (popup/tab) was closed */
    AppClosed: 'app_closed'
} as const;

/**
 * Union type of all valid BW→CS message type strings.
 */
export type BwCsMessageType = typeof BwCsMessageTypes[keyof typeof BwCsMessageTypes];

/**
 * Type guard to check if a string is a valid BW→CS message type.
 * @param type The type string to validate
 * @returns True if the type is a valid BwCsMessageType
 */
export function isBwCsMessageType(type: string): type is BwCsMessageType {
    return Object.values(BwCsMessageTypes).includes(type as BwCsMessageType);
}
