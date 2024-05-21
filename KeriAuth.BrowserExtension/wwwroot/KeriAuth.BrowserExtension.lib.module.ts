/**
* See https://mingyaulee.github.io/Blazor.BrowserExtension/app.js 
* Called before Blazor starts.
 * @param {object} options Blazor WebAssembly start options. Refer to https://github.com/dotnet/aspnetcore/blob/main/src/Components/Web.JS/src/Platform/WebAssemblyStartOptions.ts
 * @param {object} extensions Extensions added during publishing
 * @param {object} blazorBrowserExtension Blazor browser extension instance
 */

export const prefix = "KeriAuth.BrowserExtension.lib.module: ";

export function onRuntimeConfigLoaded(config: any) {
    console.log(`${prefix}onRuntimeConfigLoaded`);
}

export function beforeStart(options: any, extensions: any, blazorBrowserExtension: any) {
    console.log(`${prefix}beforeStart`);
    // See https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/startup?view=aspnetcore-8.0#javascript-initializers
    // and https://mingyaulee.github.io/Blazor.BrowserExtension/app-js
}

export async function onRuntimeReady({ }) // { getAssemblyExports, getConfig }
{
    console.log(`${prefix}onRuntimeReady`);
    console.log(`${prefix}onRuntimeReady: importing JS modules... `);
    try {
        // await import("./scripts/registerInactivityEvents.js");
        // await import("./scripts/uiHelper.js");
    }
    catch (error) {
        console.log(`${prefix}onRuntimeReady: error importing modules: ${error}`);
    }
}

/**
 * Called after Blazor is ready to receive calls from JS.
 * @param {any} blazor The Blazor instance
 */
//export async function afterStarted(blazor: any) {
//    console.log(`${prefix}afterStarted`);
//}