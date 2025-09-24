/* eslint-disable no-unused-vars */
/* eslint-disable @typescript-eslint/no-unused-vars */
/* eslint-disable @typescript-eslint/naming-convention */

// MainWorldContentScript.ts
// This script runs in the MAIN world of the webpage, and can be later expanded to provide a safe API for the page.
// However, the current websites compatable with signify-browser-extension utilize the polaris-web javascript protocol (https://github.com/WebOfTrust/polaris-web),
// which use window.postMessage() to communicate with the extension's content script (our IsolatedWorldContentScript).
//
// Therefore, this script is currently intentionally minimal and primarily serves as a placeholder to define a safe API if needed in the future.

export { }; // Make this file an external module

// Extend the Window interface to include KeriAuthApi
declare global {
    interface Window {
        KeriAuthApi?: IKeriAuthApi;
    }
}

/** Public API surface the page will see. Keep this minimal & stable. */
export interface IKeriAuthApi {
    /** Generic RPC call into the extension. */
    call<T = unknown>(method: string, ...args: unknown[]): Promise<T>;
    /** Semantic version string for compatibility checks. */
    readonly version: string;
}

// ---- Idempotency: define once in MAIN world --------------------------------
if (!window.KeriAuthApi) {
    const VERSION = '1.0.0';

    const call = <T = unknown>(method: string, ...args: unknown[]): Promise<T> =>
        new Promise<T>((resolve, reject) => {
            // placeholder
        });

    const api: IKeriAuthApi = Object.freeze({
        call,
        version: VERSION
    });

    Object.defineProperty(window, 'MyExtAPI', {
        value: api,
        enumerable: false,
        configurable: false,
        writable: false
    });

    console.log(`KeriAuth Api ${VERSION} initialized in MAIN world.`);
} else {
    console.log('KeriAuth Api already defined in MAIN world; skipping initialization.');
}
