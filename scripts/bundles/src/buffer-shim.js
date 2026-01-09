// Buffer polyfill for browser environment
// Required by signify-ts ESSR methods (wrap/unwrap) in older versions
import { Buffer } from 'buffer';

// Make Buffer globally available
if (typeof globalThis.Buffer === 'undefined') {
    globalThis.Buffer = Buffer;
}

// Also set on window/self for compatibility
if (typeof window !== 'undefined' && typeof window.Buffer === 'undefined') {
    window.Buffer = Buffer;
}
if (typeof self !== 'undefined' && typeof self.Buffer === 'undefined') {
    self.Buffer = Buffer;
}

export { Buffer };
