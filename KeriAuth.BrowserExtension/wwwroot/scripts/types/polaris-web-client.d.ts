// Temporarily adding this in KERI Auth until the following issue is resolved: https://github.com/WebOfTrust/polaris-web/issues/32
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
export interface CredentialResult {
    /**
     * If the extension responds with a credential, the data will be contained here.
     */
    credential: any;
}
export interface CreateCredentialArgs {
    /**
     * credential attributes as per schema
     */
    credData: any;
    /**
     * SAID of the schema
     *
     */
    schemaSaid: string;
}
export interface CreateCredentialResult {
    acdc: Record<string, any>;
    iss: Record<string, any>;
    anc: Record<string, any>;
    op: Record<string, any>;
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
    error?: string;
}
export interface ExtensionClientOptions {
    /**
     * The target origin for the messages.
     *
     * See https://developer.mozilla.org/en-US/docs/Web/API/Window/postMessage#targetorigin
     */
    targetOrigin?: string;
}
export declare class ExtensionClient {
    #private;
    private options;
    constructor(options: ExtensionClientOptions);
    isExtensionInstalled: (timeout?: number) => Promise<string | false>;
    /**
     * Sends a /signify/sign-request message to the extension.
     *
     * The extension decides whether or not it needs to prompt the user to approve the signing
     * or automatically sign the request.
     *
     * @param payload Information about the request that needs to be signed.
     * @returns
     */
    signRequest: (payload: SignRequestArgs) => Promise<SignRequestResult>;
    /**
     * Sends a /signify/sign-data message to the extension.
     *
     * The extension should prompt the user to select a credential or identifier to sign with.
     *
     * @param payload The arguments to pass to the extension.
     * @returns {AuthorizeResult}
     */
    signData: (payload: SignDataArgs) => Promise<SignDataResult>;
    /**
     * Sends a /signify/authorize message to the extension.
     *
     * The extension should prompt the user to select a credential or identifier,
     * on success, it should send a /signify/reply message back to the browser page.
     *
     * This method is used to start an authorized "session" with the extension. Depending
     * on the implemention, the extension can start to allow "signRequest" messages
     * after a successful authorization.
     *
     * @param payload The arguments to pass to the extension.
     * @returns {AuthorizeResult}
     */
    authorize: (payload?: AuthorizeArgs) => Promise<AuthorizeResult>;
    /**
     * Sends a /signify/authorize message to the extension.
     *
     * The extension should prompt the user to select a identifier,
     * on success, it should send a /signify/reply message back to the browser page.
     *
     * This method is used to start an authorized "session" with the extension. Depending
     * on the implemention, the extension can start to allow "signRequest" messages
     * after a successful authorization.
     *
     * @param payload The arguments to pass to the extension.
     * @returns {AuthorizeResult}
     */
    authorizeAid: (payload?: AuthorizeArgs) => Promise<AuthorizeResult>;
    /**
     * Sends a /signify/authorize message to the extension.
     *
     * The extension should prompt the user to select a credential,
     * on success, it should send a /signify/reply message back to the browser page.
     *
     * This method is used to start an authorized "session" with the extension. Depending
     * on the implemention, the extension can start to allow "signRequest" messages
     * after a successful authorization.
     *
     * @param payload The arguments to pass to the extension.
     * @returns {AuthorizeResult}
     */
    authorizeCred: (payload?: AuthorizeArgs) => Promise<AuthorizeResult>;
    /**
     * Sends a /signify/get-session-info message to the extension.
     * prereq:
     * once webapp has received an authorized result, a session is created on the extension
     *
     * Upon successfull session, this method is used to receive previously
     * selected signature conditioned to its session validity. If session is expired this would throw an error.
     * Otherwise, it returns AuthorizeResult
     *
     * @param payload The arguments to pass to the extension.
     * @returns {AuthorizeResult}
     */
    getSessionInfo: (payload?: AuthorizeArgs) => Promise<AuthorizeResult>;
    /**
     * Sends a /signify/clear-session message to the extension.
     *
     * This method is used to clear session with extension if exist.
     *
     * @param payload The arguments to pass to the extension.
     * @returns {AuthorizeResult}
     */
    clearSession: (payload?: AuthorizeArgs) => Promise<AuthorizeResult>;
    /**
      * Sends a /signify/credential/create/data-attestation message to the extension.
      *
      * The extension decides whether or not it needs to prompt the user to approve the signing
      * or automatically sign the request.
      *
      * Example of data attestation schema: https://github.com/provenant-dev/public-schema/blob/main/attestation/attestation.schema.json
      *
      * @param payload  Information about data attestation credential.
      * @returns {CreateCredentialResult}
      */
    createDataAttestationCredential: (payload: CreateCredentialArgs) => Promise<CreateCredentialResult>;
    /**
     * Sends a /signify/credential/get message to the extension.
     *
     * The extension decides whether or not it needs to prompt the user to approve the signing
     * or automatically sign the request.
     *
     * @param said  credential SAID.
     * @param includeCESR  include credential CESR stream in response.
     * @returns {CredentialResult}
     */
    getCredential: (said: string, includeCESR?: boolean) => Promise<CredentialResult>;
    /**
     * Configures the extension with the specified vendor.
     * @param payload The vendor configuration
     * @summary Tries to set the vendor url in the extension to load vendor supplied info e.g theme, logo etc.
     * @example
     * ```ts
     * await signifyClient.provideConfigUrl({url: "https://api.npoint.io/52639f849bb31823a8c0"});
     * ```
     * @remarks
     * This function is used to set the vendor url in the extension. The extension will fetch the vendor supplied info from the vendor url in json format.
     *
     * @see Template for [Vendor Loaded JSON](https://api.npoint.io/52639f849bb31823a8c0)
     */
    configureVendor: (payload?: ConfigureVendorArgs) => Promise<void>;
    /**
     * Sends an arbitrary message to the extension.
     *
     * This method can be used if there is no shorthand method implemented yet
     * for the message that needs to be sent.
     *
     * The message will always contain the "type" property and a unique "requestId".
     * The second parameter will be spread, this allows you to add any additional properties to the request.
     *
     * ```typescript
     * {
     *    "type": string,
     *    "requestId": string,
     *    ...payload
     * }
     * ```
     *
     * @param type
     * @param payload
     * @returns
     */
    sendMessage: <TRequest, TResponse>(type: string, payload?: TRequest) => Promise<TResponse>;
}
/**
 * Creates and returns a new extension client.
 * The created instance can be used to communicate with a compatible browser extension.
 *
 * @example
 * const client = createClient();
 * const authResult = await client.authorize({ message: "A message" });
 *
 * const signResult = await client.signRequest({
 *   url: "http://example.com",
 *   method: "GET"
 * });
 *
 * await fetch("http://example.com", { headers: signResult.headers })
 *
 *
 * @returns {ExtensionClient}
 */
export declare function createClient(options?: ExtensionClientOptions): ExtensionClient;
//# sourceMappingURL=client.d.ts.map