// subset from https://github.com/WebOfTrust/polaris-web/blob/main/src/client.ts  @448b5fe
export interface SessionArgs {
    oneTime?: boolean;
}

export interface AuthorizeArgs {
    /**
     * The optional message to provide to the extension
     */
    message?: string;
    /**
     * The optional message to provide to the extension
     */
    session?: SessionArgs;
}

export interface AuthorizeResultCredential {
    /**
     * The credential data
     */
    raw: unknown;

    /**
     * The credential data as a CESR encoded key event stream
     */
    cesr: string;
}

export interface AuthorizeResultIdentifier {
    /**
     * The prefix of the selected identifier
     */
    prefix: string;
}

export interface AuthorizeResult {
    /**
     * If the extension responds with a credential, the data will be contained here.
     */
    credential?: AuthorizeResultCredential;

    /**
     * If the extension responds with an identifier, the data will be contained here.
     */
    identifier?: AuthorizeResultIdentifier;

    headers?: Record<string, string>;
}

export interface SignDataArgs {
    /**
     * The optional message to provide to the extension
     */
    message?: string;

    /**
     * The data to sign as utf-8 encoded strings
     */
    items: string[];
}

export interface SignDataResultItem {
    /**
     * The data that was signed
     */
    data: string;

    /**
     * The signature
     */
    signature: string;
}

export interface SignDataResult {
    /**
     * The prefix of the AID that signed the data.
     */
    aid: string;

    /**
     * The data and the signatures
     */
    items: SignDataResultItem[];
}

export interface SignRequestArgs {
    /**
     * The URL of the request to sign.
     */
    url: string;

    /**
     * The method of the request to sign.
     *
     * @default "GET"
     */
    method?: string;

    /**
     * Optional headers of the request.
     */
    headers?: Record<string, string>;
}

export interface SignRequestResult {
    /**
     * The Signify signed headers that should be appended to the request.
     */
    headers: Record<string, string>;
}

export interface ConfigureVendorArgs {
    /**
     * The vendor url
     */
    url: string;
}

export interface MessageData<T = unknown> {
    type: string;
    requestId: string;
    payload?: T;
    error?: unknown;  // e.g. { code: 501, message: "KERIAuthCs: sessions not supported" }
}

type PendingRequest<T = unknown> = { resolve: (value: T) => void; reject: (reason: Error) => void };

export interface ExtensionClientOptions {
    /**
     * The target origin for the messages.
     *
     * See https://developer.mozilla.org/en-US/docs/Web/API/Window/postMessage#targetorigin
     */
    targetOrigin?: string;
}
