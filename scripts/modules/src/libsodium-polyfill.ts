// libsodium-polyfill.ts
// Polyfills for libsodium WASM initialization in service worker context
//
// CRITICAL: This module must load BEFORE signifyClient.js
// See commits 90aa450 and 6f33fba for context
//
// PROBLEM SOLVED:
// - libsodium WASM (used by signify-ts) initializes during ES module evaluation
// - In Chrome service workers, libsodium checks for Node.js crypto.randomBytes()
// - Service workers only have Web Crypto API (crypto.getRandomValues())
// - Blazor.BrowserExtension polyfills 'window' object in service worker
// - window.crypto was a Proxy that didn't expose getRandomValues() correctly
// - libsodium initialization failed with "No secure random number generator found"
//
// SOLUTION:
// 1. Override window.crypto to use the real self.crypto from service worker
// 2. Add crypto.randomBytes() polyfill for libsodium compatibility
// 3. Pre-configure Module.getRandomValue for Emscripten WASM

// Extend Crypto interface to include randomBytes
interface CryptoWithRandomBytes extends Crypto {
    randomBytes?: (size: number) => Uint8Array;
}

// Emscripten Module interface
interface EmscriptenModule {
    getRandomValue?: (max: number) => number;
    instantiateWasm?: unknown;
}

// Extend globalThis to include Module property
interface GlobalThis {
    Module?: EmscriptenModule;
}

// Make this a module by exporting something
export {};

// Fix 1: Override Blazor.BrowserExtension's window.crypto polyfill
// In service worker context, window is polyfilled but window.crypto doesn't work correctly
if (typeof window !== 'undefined' && typeof self !== 'undefined' && self.crypto) {
    try {
        Object.defineProperty(window, 'crypto', {
            value: self.crypto,
            writable: false,
            configurable: true
        });
        console.log('[libsodium-polyfill] Fixed window.crypto to use self.crypto');
    } catch (e) {
        console.error('[libsodium-polyfill] Failed to fix window.crypto:', e);
    }
}

// Fix 2: Add crypto.randomBytes() polyfill that libsodium looks for
// Note: self.crypto is read-only in service workers. Use Object.defineProperty
// to add randomBytes() method without triggering "Cannot set property" errors.
if (typeof self !== 'undefined' && self.crypto && !(self.crypto as CryptoWithRandomBytes).randomBytes) {
    try {
        Object.defineProperty(self.crypto, 'randomBytes', {
            value: (size: number) => {
                const buffer = new Uint8Array(size);
                self.crypto.getRandomValues(buffer);
                return buffer;
            },
            writable: false,
            configurable: true
        });
        console.log('[libsodium-polyfill] Added crypto.randomBytes()');
    } catch (e) {
        console.error('[libsodium-polyfill] Failed to add crypto.randomBytes:', e);
    }
}

// Also ensure globalThis.crypto has randomBytes (belt-and-suspenders)
if (typeof globalThis !== 'undefined' && globalThis.crypto && !(globalThis.crypto as CryptoWithRandomBytes).randomBytes) {
    try {
        Object.defineProperty(globalThis.crypto, 'randomBytes', {
            value: (size: number) => {
                const buffer = new Uint8Array(size);
                globalThis.crypto.getRandomValues(buffer);
                return buffer;
            },
            writable: false,
            configurable: true
        });
    } catch (e) {
        // Silently ignore - globalThis.crypto might be read-only in some contexts
    }
}

// Fix 3: Pre-configure Module object for Emscripten WASM (used by libsodium)
if (typeof self !== 'undefined' && !(globalThis as GlobalThis).Module) {
    (globalThis as GlobalThis).Module = {
        // Ensure WASM uses browser crypto for randomness
        getRandomValue: function(max: number): number {
            if (self.crypto && self.crypto.getRandomValues) {
                // Use cryptographically secure random
                const array = new Uint32Array(1);
                self.crypto.getRandomValues(array);
                return array[0]! % max;
            }
            // Fallback to Math.random (not cryptographically secure, but better than nothing)
            return Math.floor(Math.random() * max);
        },
        // Let libsodium use its default base64 embedded WASM
        instantiateWasm: undefined
    };
    console.log('[libsodium-polyfill] Configured Module.getRandomValue');
}

console.log('[libsodium-polyfill] All polyfills initialized for service worker context');
