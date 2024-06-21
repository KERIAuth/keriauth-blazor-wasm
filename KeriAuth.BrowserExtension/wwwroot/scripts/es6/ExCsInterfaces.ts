//
// Common definitions for this content script and the extension service-worker.
// Note these are manually repeated here and in the ContentScript,
// because of the CommonJS module system that must be used for this ContentScript.
// A fix would be to use a separate CommonInterface.ts file and a bundler to build the content script, but that is not yet implemented.
//

// Message types from CS to SW
export type CsSwMsg = ICsSwMsgSelectIdentifier | ICsSwMsgSelectCredential;

export interface ICsSwMsg {
    type: string
}

export enum CsSwMsgType {
    SELECT_IDENTIFIER = "select-identifier",
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
    type: CsSwMsgType.SELECT_IDENTIFIER
}

export interface ICsSwMsgHello extends ICsSwMsg {
    type: CsSwMsgType.DOMCONTENTLOADED
}

export interface ICsSwMsgSelectCredential extends ICsSwMsg {
    type: CsSwMsgType.SELECT_CREDENTIAL
    data: any
}

// Message types from Extension to CS
export interface IExCsMsg {
    type: ExCsMsgType
}

export enum ExCsMsgType {
    HELLO = "hello"
}

export interface IExCsMsgHello extends IExCsMsg {
    type: ExCsMsgType.HELLO
}