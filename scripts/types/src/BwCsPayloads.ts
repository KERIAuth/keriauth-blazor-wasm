/**
 * Typed payloads for BackgroundWorker → ContentScript communication,
 * and factory functions for ContentScript → Page messages.
 *
 * Naming convention:
 * - BwCs = BackgroundWorker → ContentScript direction
 * - CsPage = ContentScript → Page direction (via window.postMessage)
 */

import { BwCsMessageTypes, type BwCsMessageType } from './CsBwRpcMethods.js';
import { CsPageMsgTag } from './ExCsInterfaces.js';
import type * as Polaris from './polaris-web-client.js';

// Re-export CsPageMsgTag for convenience (originally defined in ExCsInterfaces.ts)
export { CsPageMsgTag };

// ============================================================================
// CS→Page Message Types (polaris-web protocol)
// ============================================================================

/**
 * Base interface for ContentScript → Page messages.
 * Follows polaris-web MessageData structure with source identifier.
 *
 * Property names are defined by polaris-web protocol and must not be changed.
 */
export interface CsPageMessage<T = unknown> {
    /** Source identifier - always 'KeriAuthCs' */
    source: typeof CsPageMsgTag;
    /** Message type - matches polaris-web protocol */
    type: string;
    /** Request ID for correlating request/response */
    requestId?: string;
    /** Success payload (mutually exclusive with error) */
    payload?: T;
    /** Error message (mutually exclusive with payload) */
    error?: string;
}

/**
 * Success message from CS to Page with typed payload.
 */
export interface CsPageSuccessMessage<T> extends CsPageMessage<T> {
    payload: T;
    error?: never;
}

/**
 * Error message from CS to Page.
 */
export interface CsPageErrorMessage extends CsPageMessage<never> {
    payload?: never;
    error: string;
}

// ============================================================================
// Factory Functions for CS→Page Messages
// ============================================================================

/**
 * Creates a success reply message from ContentScript to Page.
 * Used when BW responds with a successful result.
 *
 * @param requestId The original request ID from the page
 * @param payload The success payload
 * @returns A typed CsPageSuccessMessage
 *
 * @example
 * const msg = createCsPageSuccessReply(requestId, authorizeResult);
 * window.postMessage(msg, origin);
 */
export function createCsPageSuccessReply<T>(
    requestId: string,
    payload: T
): CsPageSuccessMessage<T> {
    return {
        source: CsPageMsgTag,
        type: BwCsMessageTypes.Reply,
        requestId,
        payload
    };
}

/**
 * Creates an error reply message from ContentScript to Page.
 * Used when BW responds with an error or operation was canceled.
 *
 * @param requestId The original request ID from the page
 * @param error The error message
 * @param type Optional message type (defaults to Reply)
 * @returns A typed CsPageErrorMessage
 *
 * @example
 * const msg = createCsPageErrorReply(requestId, 'User canceled');
 * window.postMessage(msg, origin);
 */
export function createCsPageErrorReply(
    requestId: string,
    error: string,
    type: BwCsMessageType = BwCsMessageTypes.Reply
): CsPageErrorMessage {
    return {
        source: CsPageMsgTag,
        type,
        requestId,
        error
    };
}

/**
 * Creates a canceled reply message from ContentScript to Page.
 * Used when user cancels an operation.
 *
 * @param requestId The original request ID from the page
 * @param message Optional cancellation message
 * @returns A typed CsPageErrorMessage
 */
export function createCsPageCanceledReply(
    requestId: string,
    message: string = 'User canceled'
): CsPageErrorMessage {
    return createCsPageErrorReply(requestId, message, BwCsMessageTypes.Reply);
}

// ============================================================================
// Specific Response Payload Types
// ============================================================================

/**
 * Payload for authorize success response.
 * Matches polaris-web AuthorizeResult.
 */
export type BwCsAuthorizePayload = Polaris.AuthorizeResult;

/**
 * Payload for sign-data success response.
 * Matches polaris-web SignDataResult.
 */
export type BwCsSignDataPayload = Polaris.SignDataResult;

/**
 * Payload for sign-request success response.
 * Matches polaris-web SignRequestResult.
 */
export type BwCsSignRequestPayload = Polaris.SignRequestResult;

/**
 * Payload for create-data-attestation success response.
 * Matches polaris-web CreateCredentialResult.
 */
export type BwCsCreateCredentialPayload = Polaris.CreateCredentialResult;

/**
 * Payload for get-credential success response.
 * Matches polaris-web CredentialResult.
 */
export type BwCsGetCredentialPayload = Polaris.CredentialResult;

/**
 * Payload for signify-extension query response.
 */
export interface BwCsExtensionIdPayload {
    extensionId: string;
}

// ============================================================================
// Typed Response Message Types
// ============================================================================

/**
 * Authorize success message to Page.
 */
export type CsPageAuthorizeSuccessMessage = CsPageSuccessMessage<BwCsAuthorizePayload>;

/**
 * Sign-data success message to Page.
 */
export type CsPageSignDataSuccessMessage = CsPageSuccessMessage<BwCsSignDataPayload>;

/**
 * Sign-request success message to Page.
 */
export type CsPageSignRequestSuccessMessage = CsPageSuccessMessage<BwCsSignRequestPayload>;

/**
 * Create-credential success message to Page.
 */
export type CsPageCreateCredentialSuccessMessage = CsPageSuccessMessage<BwCsCreateCredentialPayload>;

// ============================================================================
// Legacy Compatibility (RpcResponse to CsPageMessage conversion)
// ============================================================================

/**
 * Converts an RpcResponse result to a CsPageMessage.
 * Used by ContentScript to transform BW responses for the Page.
 *
 * @param requestId The original page request ID
 * @param ok Whether the RPC was successful
 * @param result The success result (if ok is true)
 * @param error The error message (if ok is false)
 * @returns A CsPageMessage ready to post to the page
 */
export function rpcResponseToCsPageMessage<T>(
    requestId: string,
    ok: boolean,
    result?: T,
    error?: string
): CsPageMessage<T> {
    if (ok && result !== undefined) {
        return createCsPageSuccessReply(requestId, result);
    } else {
        return createCsPageErrorReply(requestId, error ?? 'Unknown error');
    }
}
