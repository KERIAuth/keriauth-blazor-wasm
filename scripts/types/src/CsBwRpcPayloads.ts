/**
 * Typed RPC request params and response results for CS→BW communication.
 * Maps each CsBwRpcMethod to its expected params and result types.
 *
 * These types are used to ensure compile-time type safety when constructing
 * RPC requests and handling responses.
 */

import type { CsBwRpcMethods, CsBwRpcMethod } from './CsBwRpcMethods.js';
import { CsBwPortMessageTypes } from './CsBwRpcMethods.js';
import type * as Polaris from './polaris-web-client.js';

// ============================================================================
// Request Params Types (CS→BW)
// ============================================================================

/**
 * Params for /signify/authorize request.
 * Polaris-web AuthorizeArgs.
 */
export type CsBwAuthorizeParams = Polaris.AuthorizeArgs;

/**
 * Params for /signify/authorize/aid request.
 * Same as AuthorizeArgs but specifically for AID selection.
 */
export type CsBwSelectAuthorizeAidParams = Polaris.AuthorizeArgs;

/**
 * Params for /signify/authorize/credential request.
 * Same as AuthorizeArgs but specifically for credential selection.
 */
export type CsBwSelectAuthorizeCredentialParams = Polaris.AuthorizeArgs;

/**
 * Params for /signify/sign-data request.
 */
export type CsBwSignDataParams = Polaris.SignDataArgs;

/**
 * Params for /signify/sign-request request.
 */
export type CsBwSignRequestParams = Polaris.SignRequestArgs;

/**
 * Params for /signify/credential/create/data-attestation request.
 */
export type CsBwCreateDataAttestationParams = Polaris.CreateCredentialArgs;

/**
 * Params for /signify/credential/get request.
 */
export interface CsBwGetCredentialParams {
    /** The SAID of the credential to retrieve */
    said: string;
    /** Whether to include CESR stream in response */
    includeCESR?: boolean;
}

/**
 * Params for /signify/configure-vendor request.
 */
export type CsBwConfigureVendorParams = Polaris.ConfigureVendorArgs;

/**
 * Params for signify-extension query (no params required).
 */
export type CsBwSignifyExtensionParams = undefined;

/**
 * Params for init message (no params required).
 */
export type CsBwInitParams = undefined;

/**
 * Params for /KeriAuth/connection/invite request.
 * Page sends its OOBI to initiate a mutual connection.
 */
export interface CsBwConnectionInviteParams {
    /** Page's OOBI to share with the extension */
    oobi: string;
}

/**
 * Params for /KeriAuth/connection/confirm notification.
 * Page confirms it resolved the reciprocal OOBI (or reports failure).
 */
export interface CsBwConnectionConfirmParams {
    /** The original OOBI the page sent in the invite (correlation key) */
    oobi: string;
    /** Non-empty if the page failed to resolve the reciprocal OOBI */
    error?: string;
}

// ============================================================================
// Response Result Types (BW→CS via RpcResponse)
// ============================================================================

/**
 * Result for /signify/authorize response.
 */
export type CsBwAuthorizeResult = Polaris.AuthorizeResult;

/**
 * Result for /signify/sign-data response.
 */
export type CsBwSignDataResult = Polaris.SignDataResult;

/**
 * Result for /signify/sign-request response.
 */
export type CsBwSignRequestResult = Polaris.SignRequestResult;

/**
 * Result for /signify/credential/create/data-attestation response.
 */
export type CsBwCreateDataAttestationResult = Polaris.CreateCredentialResult;

/**
 * Result for /signify/credential/get response.
 */
export type CsBwGetCredentialResult = Polaris.CredentialResult;

/**
 * Result for signify-extension query.
 */
export interface CsBwSignifyExtensionResult {
    extensionId: string;
}

/**
 * Result for /KeriAuth/connection/invite response.
 * Extension's reciprocal OOBI for the page to resolve.
 */
export interface CsBwConnectionInviteResult {
    /** Extension's reciprocal OOBI (always present on success) */
    oobi: string;
}

// ============================================================================
// Method→Params Type Map
// ============================================================================

/**
 * Maps each CS→BW RPC method to its expected params type.
 * Use this for compile-time type checking of RPC request construction.
 *
 * @example
 * function sendRequest<M extends CsBwRpcMethod>(
 *     method: M,
 *     params: CsBwRpcParamsMap[M]
 * ): void { ... }
 */
export interface CsBwRpcParamsMap {
    [CsBwRpcMethods.Authorize]: CsBwAuthorizeParams;
    [CsBwRpcMethods.SelectAuthorizeAid]: CsBwSelectAuthorizeAidParams;
    [CsBwRpcMethods.SelectAuthorizeCredential]: CsBwSelectAuthorizeCredentialParams;
    [CsBwRpcMethods.SignData]: CsBwSignDataParams;
    [CsBwRpcMethods.SignRequest]: CsBwSignRequestParams;
    [CsBwRpcMethods.CreateDataAttestation]: CsBwCreateDataAttestationParams;
    [CsBwRpcMethods.GetCredential]: CsBwGetCredentialParams;
    [CsBwRpcMethods.ConfigureVendor]: CsBwConfigureVendorParams;
    [CsBwRpcMethods.SignifyExtension]: CsBwSignifyExtensionParams;
    [CsBwRpcMethods.SignifyExtensionClient]: CsBwSignifyExtensionParams;
    [CsBwRpcMethods.GetSessionInfo]: Polaris.AuthorizeArgs;
    [CsBwRpcMethods.ClearSession]: Polaris.AuthorizeArgs;
    [CsBwRpcMethods.ConnectionInvite]: CsBwConnectionInviteParams;
    [CsBwRpcMethods.ConnectionConfirm]: CsBwConnectionConfirmParams;
    [CsBwRpcMethods.Init]: CsBwInitParams;
}

// ============================================================================
// Method→Result Type Map
// ============================================================================

/**
 * Maps each CS→BW RPC method to its expected result type.
 * Use this for compile-time type checking of RPC response handling.
 *
 * @example
 * function handleResponse<M extends CsBwRpcMethod>(
 *     method: M,
 *     result: CsBwRpcResultMap[M]
 * ): void { ... }
 */
export interface CsBwRpcResultMap {
    [CsBwRpcMethods.Authorize]: CsBwAuthorizeResult;
    [CsBwRpcMethods.SelectAuthorizeAid]: CsBwAuthorizeResult;
    [CsBwRpcMethods.SelectAuthorizeCredential]: CsBwAuthorizeResult;
    [CsBwRpcMethods.SignData]: CsBwSignDataResult;
    [CsBwRpcMethods.SignRequest]: CsBwSignRequestResult;
    [CsBwRpcMethods.CreateDataAttestation]: CsBwCreateDataAttestationResult;
    [CsBwRpcMethods.GetCredential]: CsBwGetCredentialResult;
    [CsBwRpcMethods.ConfigureVendor]: void;
    [CsBwRpcMethods.SignifyExtension]: CsBwSignifyExtensionResult;
    [CsBwRpcMethods.SignifyExtensionClient]: CsBwSignifyExtensionResult;
    [CsBwRpcMethods.GetSessionInfo]: CsBwAuthorizeResult;
    [CsBwRpcMethods.ClearSession]: CsBwAuthorizeResult;
    [CsBwRpcMethods.ConnectionInvite]: CsBwConnectionInviteResult;
    [CsBwRpcMethods.ConnectionConfirm]: void;
    [CsBwRpcMethods.Init]: void;
}

// ============================================================================
// Typed RPC Request Interface
// ============================================================================

/**
 * Strongly-typed RPC request for a specific CS→BW method.
 * The params type is inferred from the method.
 * Uses directional discriminator 'CS_BW_RPC_REQ' for log clarity.
 */
export interface TypedCsBwRpcRequest<M extends CsBwRpcMethod> {
    t: typeof CsBwPortMessageTypes.RpcRequest;
    portSessionId: string;
    id: string;
    method: M;
    params?: CsBwRpcParamsMap[M];
}

/**
 * Factory to create a typed CS→BW RPC request.
 * Provides compile-time type checking that params match the method.
 *
 * @param portSessionId The port session ID
 * @param method The RPC method (from CsBwRpcMethods)
 * @param params The method-specific params
 * @param id Optional request ID (auto-generated if not provided)
 * @returns A typed RPC request object
 *
 * @example
 * const request = createTypedCsBwRpcRequest(
 *     sessionId,
 *     CsBwRpcMethods.SignData,
 *     { items: ['data to sign'], message: 'Please sign' }
 * );
 */
export function createTypedCsBwRpcRequest<M extends CsBwRpcMethod>(
    portSessionId: string,
    method: M,
    params?: CsBwRpcParamsMap[M],
    id?: string
): TypedCsBwRpcRequest<M> {
    return {
        t: CsBwPortMessageTypes.RpcRequest,
        portSessionId,
        id: id ?? crypto.randomUUID(),
        method,
        params
    };
}
