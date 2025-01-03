// Common definitions for content script and service-worker.

import * as Polaris from "../types/polaris-web-client"

export interface ICsSwMsg {
    type: string
    requestId?: string
    payload?: object
}

// Message types from Page to CS, which may be then forwarded to the extension service-worker.  Aka "EVENT_TYPE" in the polaris-web code."
export enum CsSwMsgType {
    POLARIS_SIGNIFY_EXTENSION = "signify-extension",
    POLARIS_SIGNIFY_EXTENSION_CLIENT = "signify-extension-client",
    POLARIS_CONFIGURE_VENDOR = "/signify/configure-vendor",
    POLARIS_SIGNIFY_AUTHORIZE = "/signify/authorize",
    POLARIS_SELECT_AUTHORIZE_AID = "/signify/authorize/aid",
    POLARIS_SELECT_AUTHORIZE_CREDENTIAL = "/signify/authorize/credential",
    POLARIS_SIGN_DATA = "/signify/sign-data",
    POLARIS_SIGN_REQUEST = "/signify/sign-request",
    POLARIS_GET_SESSION_INFO = "/signify/get-session-info",
    POLARIS_CLEAR_SESSION = "/signify/clear-session",
    POLARIS_CREATE_DATA_ATTESTATION = "/signify/credential/create/data-attestation",
    POLARIS_GET_CREDENTIAL = "/signify/credential/get"
}

// Message types from Extension to CS (and typically forward to Page and sometimes of type FooResponse)
export interface ISwCsMsg {
    type: string // SwCsMsgType
    requestId?: string // response to this requestId
    payload?: object
    error?: string
}

export enum SwCsMsgType {
    HELLO = "hello",
    CANCELED = "canceled",
    REPLY = "/signify/reply",
    FSW = "fromServiceWorker",
    // SE = "signify-extension"
}

export interface IExCsMsgHello extends ISwCsMsg {
    type: SwCsMsgType.HELLO
}

// This IIdentifier is used in the context of responses from the extension service-worker, CS, to page
export interface IIdentifier {
    name?: string;
    prefix: string;
}

// This ICredential is used in the context of responses from the extension service-worker, CS, to page
export interface ICredential {
    issueeName: string;
    ancatc: string[];
    sad: { a: { i: string }; d: string };
    schema: {
        title: string;
        credentialType: string;
        description: string;
    };
    status: {
        et: string;
    };
    cesr?: string;
}

// This ISignature is used in the context of responses from the extension service-worker, CS, to page
export interface ISignature {
    headers: HeadersInit;
    credential?: ICredential;
    identifier?: {
        name?: string;
        prefix?: string;
    };
    autoSignin?: boolean;
}

// See also ReplyMessageData.cs, which pairs with this interface for interop with the extension WASM.
// The generic type T is the payload, which is typically a response object from the extension service-worker.
// See MessageData < T > from polaris - web / types, and see the interfaces that extend MessageData<T>, in the current file.
export interface ReplyMessageData<T = unknown> {
    type: string;
    requestId: string;
    payload?: T;
    error?: unknown;  // e.g. { code: 501, message: "KERIAuthCs: sessions not supported" }
    payloadTypeName?: string;
    source?: string;
}

export const CsToPageMsgIndicator = "KeriAuthCs";

// This interface helps shape ContentScript messages to tab
export interface KeriAuthToPolarisMessageData<T> extends Polaris.MessageData<T> {
    source: typeof CsToPageMsgIndicator;
}

// Signing related types from signify-browser-extension config/types.ts. Here because we don't want dependencies on signify-browser-extension,
// and ISignin is not defined in polaris-web/types.
export interface ISignin {
    id: string;
    domain: string;
    identifier?: {
        name?: string;
        prefix?: string;
    };
    credential?: ICredential;
    createdAt: number;
    updatedAt: number;
    autoSignin?: boolean;
}

// TODO P2 Add constructors here for the various message interfaces such as ApprovedSignRequest, to ensure they are properly formatted and optional null value combos are assured valid.

export interface ApprovedSignRequest {
    originStr: string;
    url: string;
    method: string;
    // not using type of Headers or Map<string, string> because of serialization issues.
    initHeadersDict?: { [key: string]: string };  
    selectedName: string;
}
