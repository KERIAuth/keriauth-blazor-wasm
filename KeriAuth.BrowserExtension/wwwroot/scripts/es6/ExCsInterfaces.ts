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

//
// Signing related types from signify-browser-extension. https://github.com/WebOfTrust/signify-browser-extension/blob/909803e6ad0a1038aa8d4ffea914767d98ea2894/src/config/types.ts
//
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