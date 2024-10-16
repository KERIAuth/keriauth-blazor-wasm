export interface SessionArgs {
    oneTime?: boolean;
}
export interface AuthorizeArgs {
    message?: string;
    session?: SessionArgs;
}
export interface AuthorizeResultCredential {
    raw: unknown;
    cesr: string;
}
export interface AuthorizeResultIdentifier {
    prefix: string;
}
export interface AuthorizeResult {
    credential?: AuthorizeResultCredential;
    identifier?: AuthorizeResultIdentifier;
    headers?: Record<string, string>;
}
export interface SignDataArgs {
    message?: string;
    items: string[];
}
export interface SignDataResultItem {
    data: string;
    signature: string;
}
export interface SignDataResult {
    aid: string;
    items: SignDataResultItem[];
}
export interface SignRequestArgs {
    url: string;
    method?: string;
    headers?: Record<string, string>;
}
export interface SignRequestResult {
    headers: Record<string, string>;
}
export interface ConfigureVendorArgs {
    url: string;
}
export interface MessageData<T = unknown> {
    type: string;
    requestId: string;
    payload?: T;
    error?: string;
}
export interface ExtensionClientOptions {
    targetOrigin?: string;
}
