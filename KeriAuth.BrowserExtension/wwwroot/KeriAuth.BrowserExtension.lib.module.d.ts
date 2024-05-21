/**
* See https://mingyaulee.github.io/Blazor.BrowserExtension/app.js
* Called before Blazor starts.
 * @param {object} options Blazor WebAssembly start options. Refer to https://github.com/dotnet/aspnetcore/blob/main/src/Components/Web.JS/src/Platform/WebAssemblyStartOptions.ts
 * @param {object} extensions Extensions added during publishing
 * @param {object} blazorBrowserExtension Blazor browser extension instance
 */
export declare const prefix = "KeriAuth.BrowserExtension.lib.module: ";
export declare function onRuntimeConfigLoaded(config: any): void;
export declare function beforeStart(options: any, extensions: any, blazorBrowserExtension: any): void;
export declare function onRuntimeReady({}: {}): Promise<void>;
/**
 * Called after Blazor is ready to receive calls from JS.
 * @param {any} blazor The Blazor instance
 */
