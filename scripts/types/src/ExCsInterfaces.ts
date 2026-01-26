// Common definitions for content script and BackgroundWorker.
// Types for message passing between ContentScript, BackgroundWorker, and web pages.

import type * as Polaris from './polaris-web-client';

// Re-export Polaris types for convenience
export type { Polaris };

// Message types from Page to CS, which may be then forwarded to the extension BackgroundWorker.
// Aka "EVENT_TYPE" in the polaris-web code.
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

// Message types from Extension BackgroundWorker to CS (and typically forwarded to Page)
export enum BwCsMsgEnum {
    READY = 'ready',
    REPLY_CANCELED = 'reply_canceled',
    REPLY = '/signify/reply',
    FSW = 'fromBackgroundWorker',
    APP_CLOSED = 'app_closed',
    REPLY_CRED = '/KeriAuth/signify/replyCredential'
}

// Constant used to identify messages originating from ContentScript
export const CsPageMsgTag = 'KeriAuthCs';

// Interface for ContentScript messages to the web page with typed payload
export interface ICsPageMsgData<T> {
    source: typeof CsPageMsgTag;
    type: string;
    requestId?: string;
    payload?: T;
    error?: string;
}

// Interface for ContentScript messages to the web page with a data field
export interface ICsPageMsgDataData<T> extends Omit<ICsPageMsgData<T>, 'data'> {
    data?: T;
}
