// Common definitions for content script and service-worker.

import {
    AuthorizeResultCredential,
    AuthorizeArgs,
    AuthorizeResultIdentifier,
    AuthorizeResult,
    SignDataArgs,
    SignDataResultItem,
    SignDataResult,
    SignRequestArgs,
    SignRequestResult,
    ConfigureVendorArgs,
    MessageData
} from "polaris-web/dist/client";

// Message types from CS to SW
export type CsSwMsg = ICsSwMsgSelectIdentifier | ICsSwMsgSelectCredential;

// TODO EE! See KeriAuthMessageData and MessageData. Clarify these as IXXXX and which are owned by polaris-web vs KeriAuth. Superset of these is a "Response"?
export interface ICsSwMsg {
    type: string
    requestId?: string
    payload?: object
    error?: string
}

export enum CsSwMsgType {
    SELECT_AUTHORIZE = "/signify/authorize",
    SIGN_REQUEST = "/signify/sign-request",
    SIGN_DATA = "/signify/sign-data",
    SELECT_CREDENTIAL = "select-credential",
    SIGNIFY_EXTENSION = "signify-extension",
    SELECT_ID_CRED = "select-aid-or-credential",
    SELECT_AUTO_SIGNIN = "select-auto-signin",
    NONE = "none",
    VENDOR_INFO = "vendor-info",
    FETCH_RESOURCE = "fetch-resource",
    AUTO_SIGNIN_SIG = "auto-signin-sig",
    DOMCONTENTLOADED = "document-loaded"
}

export interface ICsSwMsgSelectIdentifier extends ICsSwMsg {
    type: CsSwMsgType.SELECT_AUTHORIZE
    requestId: string
    payload: object
}

export interface ICsSwMsgHello extends ICsSwMsg {
    type: CsSwMsgType.DOMCONTENTLOADED
}

export interface ICsSwMsgSelectCredential extends ICsSwMsg {
    type: CsSwMsgType.SELECT_CREDENTIAL
    data: any
}


// Message types from Extension to CS
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
    FSW = "fromServiceWorker"
}

export interface IExCsMsgHello extends ISwCsMsg {
    type: SwCsMsgType.HELLO
}

export interface IExCsMsgCanceled extends ISwCsMsg {
    type: SwCsMsgType.CANCELED
}
export interface IExCsMsgReply extends ISwCsMsg {
    type: SwCsMsgType.REPLY
}

export interface IIdentifier {
    name?: string;
    prefix: string;
}
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

export interface ISignature {
    headers: HeadersInit;
    credential?: ICredential;
    identifier?: {
        name?: string;
        prefix?: string;
    };
    autoSignin?: boolean;
}

// See also ReplyMessageData.cs, which pairs with this interface for interop with the extension WASM
export interface ReplyMessageData<T = unknown> {
    type: string;
    requestId: string;
    payload?: T;
    error?: string;
    payloadTypeName?: string;
    source?: string;
}

export const CsToPageMsgIndicator = "KeriAuthCs";

export interface KeriAuthMessageData<T = unknown> extends MessageData<T> {
    source: typeof CsToPageMsgIndicator;
}

// Signing related types from signify-browser-extension config/types.ts
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

