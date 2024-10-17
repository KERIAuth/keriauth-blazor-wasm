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
} from "../es6/PageCsInterfaces.js"

export interface ICsSwMsg {
    type: string
    requestId?: string
    payload?: object
}

// Message types from Page to CS, which may be then forwarded to the extension service-worker.  Aka "EVENT_TYPE" in the polaris-web code."
export enum CsSwMsgType {
    AUTHORIZE_AUTO_SIGNIN = "/signify/authorize-auto-signin",
    AUTO_SIGNIN_SIG = "auto-signin-sig",
    CONFIGURE_VENDOR = "/signify/configure-vendor",
    DOMCONTENTLOADED = "document-loaded",
    FETCH_RESOURCE = "fetch-resource",
    SELECT_AUTHORIZE = "/signify/authorize",
    SELECT_AUTO_SIGNIN = "select-auto-signin",
    SELECT_AUTHORIZE_CREDENTIAL = "/signify/authorize/credential",
    SELECT_ID_CRED = "select-aid-or-credential",
    SELECT_AUTHORIZE_AID = "/signify/authorize/aid",
    SIGN_DATA = "/signify/sign-data",
    SIGN_REQUEST = "/signify/sign-request",
    SIGNIFY_AUTHORIZE = "/signify/authorize",
    SIGNIFY_EXTENSION = "signify-extension",
    VENDOR_INFO = "vendor-info",
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
    SE = "signify-extension"
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
    error?: string;
    payloadTypeName?: string;
    source?: string;
}

export const CsToPageMsgIndicator = "KeriAuthCs";

export interface KeriAuthMessageData<T = unknown> extends MessageData<T> {
    source: typeof CsToPageMsgIndicator;
    rurl?: string;
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

// TODO Add constructors here for the various message interfaces, to ensure they are properly formatted and optional null value combos are assured valid.

export interface ApprovedSignRequest {
    originStr: string;
    rUrl: string;
    rMethod: string;
    // TODO EE! lossless during serialization?
    initHeadersDict?: Map<string, string>;
    selectedName: string;
}
