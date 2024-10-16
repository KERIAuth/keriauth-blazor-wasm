# KERI Auth Browser Extension
# Page <-> Content Script Messages
Summary of requests from web page to content script, and expected replies back

| Page -> Content Script Request | Request Arg Type | Content Script -> Page Reply | Reply Type | Comments | 
| ------------- | 
|  |  | signify/reply | MessageData\<T> |  | 
|  |  | signify-extension | {type: 'signify-extension', data: { string: extensionId } } |  | 
| signify/configure-vendor | ConfigureVendorArgs | n/a | void |  | 
| signify/sign-request | SignRequestArgs | signify/reply | MessageData\<SignDataResult> |  | 
| signify/authorize | AuthorizeArgs | signify/reply | MessageData\<AuthorizeResult> |  | 
| signify/sign-data | SignDataArgs | signify/reply | MessageData\<SignDataResult> |  | 
| signify-extension-client | ExtensionClientOptions | signify/reply | MessageData\<void> |  | 
| signify/authorize/aid |  | signify/reply | MessageData\<AuthorizeResultIdentifier> |  | 
| signify/authorize/credential |  | signify/reply | MessageData\<AuthorizeResultCredential> |  | 

### References
- [KERI Auth Content Script](https://github.com/KERIAuth/keriauth-blazor-wasm/blob/main/KeriAuth.BrowserExtension/wwwroot/scripts/esbuild/ContentScript.ts)
- [Signify-browser-extension event-types](https://github.com/WebOfTrust/signify-browser-extension/blob/main/src/config/event-types.ts)
- [Polaris-web Page Messages](https://github.com/WebOfTrust/polaris-web/src/client.ts)