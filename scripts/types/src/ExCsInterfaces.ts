// Common definitions for content script and BackgroundWorker.

import type * as Polaris from './polaris-web-client';

export interface ICsBwMsg {
    type: string
    requestId?: string
    payload?: object
}

// Internal message types from ContentScript to BackgroundWorker (not from web page).
// These are used for extension-internal communication.
export enum CsInternalMsgEnum {
    /** Sent by ContentScript when it initializes to notify service worker it's ready */
    CS_READY = 'cs-ready'
}

/**
 * Message sent by ContentScript to notify the service worker that it has initialized.
 * This allows the service worker to update the tab's icon to indicate the content script is active.
 * Handled entirely in app.ts (JavaScript) - the C# BackgroundWorker ignores this message.
 */
export interface ICsReadyMessage {
    type: CsInternalMsgEnum.CS_READY;
}

// Message types from Page to CS, which may be then forwarded to the extension BackgroundWorker.  Aka "EVENT_TYPE" in the polaris-web code."
export enum CsBwMsgEnum {
    POLARIS_SIGNIFY_EXTENSION = 'signify-extension',
    POLARIS_SIGNIFY_EXTENSION_CLIENT = 'signify-extension-client',
    POLARIS_CONFIGURE_VENDOR = '/signify/configure-vendor',
    POLARIS_SIGNIFY_AUTHORIZE = '/signify/authorize',
    POLARIS_SELECT_AUTHORIZE_AID = '/signify/authorize/aid',
    POLARIS_SELECT_AUTHORIZE_CREDENTIAL = '/signify/authorize/credential',
    POLARIS_SIGN_DATA = '/signify/sign-data',
    POLARIS_SIGN_REQUEST = '/signify/sign-request',
    POLARIS_GET_SESSION_INFO = '/signify/get-session-info',
    POLARIS_CLEAR_SESSION = '/signify/clear-session',
    POLARIS_CREATE_DATA_ATTESTATION = '/signify/credential/create/data-attestation',
    POLARIS_GET_CREDENTIAL = '/signify/credential/get',
    INIT = 'init'
}

// Message types from Extension to CS (and typically forward to Page and sometimes of type FooResponse)
export interface IBwCsMsg {
    type: string // BwCsMsgEnum  // TODO P2 typof BwCsMsgEnum ?
    requestId?: string // response to this requestId
    data?: object // The payload data from BackgroundWorker
    error?: string
}

export enum BwCsMsgEnum {
    READY = 'ready',
    REPLY_CANCELED = 'reply_canceled',
    REPLY = '/signify/reply',
    FSW = 'fromBackgroundWorker',
    APP_CLOSED = 'app_closed',
    REPLY_CRED = '/KeriAuth/signify/replyCredential'
};

export interface IBwCsMsgPong extends IBwCsMsg {
    type: BwCsMsgEnum.READY
}

// This IIdentifier is used in the context of responses from the extension BackgroundWorker, CS, to page
export interface IIdentifier {
    name?: string;
    prefix: string;
}

// This ICredential is used in the context of responses from the extension BackgroundWorker, CS, to page
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

// This ISignature is used in the context of responses from the extension BackgroundWorker, CS, to page
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
// The generic type T is the payload, which is typically a response object from the extension BackgroundWorker.
// See MessageData < T > from polaris - web / types, and see the interfaces that extend MessageData<T>, in the current file.
export interface IReplyMessageData<T = unknown> {
    type: string;
    requestId: string;
    payload?: T;
    error?: unknown;  // e.g. { code: 501, message: "KERIAuthCs: sessions not supported" }
    payloadTypeName?: string;
    source?: string;
}

export const CsPageMsgTag = 'KeriAuthCs';

// This interface helps shape ContentScript messages to tab that may have a payload and/or error
export interface ICsPageMsgData<T> {
    source: typeof CsPageMsgTag;
    type: string;
    requestId?: string;
    payload?: T;
    error?: string;
}

// This interface helps shape ContentScript messages to tab with a specific data type
export interface ICsPageMsgDataData<T> extends Omit<ICsPageMsgData<T>, 'data'> {
    data?: T;
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

export interface IApprovedSignRequest {
    originStr: string;
    url: string;
    method: string;
    // not using type of Headers or Map<string, string> because of serialization issues.
    initHeadersDict?: { [key: string]: string };
    selectedName: string;
}
