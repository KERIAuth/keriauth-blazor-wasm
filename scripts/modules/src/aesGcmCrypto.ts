/**
 * Minimal AES-GCM encryption/decryption module using browser SubtleCrypto.
 * Provides low-level control over key and nonce for WebAuthn PRF-based encryption.
 */

/**
 * Helper function to convert base64 to ArrayBuffer
 */
function base64ToArrayBuffer(base64: string): ArrayBuffer {
    const binaryString = atob(base64);
    const bytes = new Uint8Array(binaryString.length);
    for (let i = 0; i < binaryString.length; i++) {
        bytes[i] = binaryString.charCodeAt(i);
    }
    return bytes.buffer as ArrayBuffer;
}

/**
 * Helper function to convert ArrayBuffer to base64
 */
function arrayBufferToBase64(buffer: ArrayBuffer): string {
    const bytes = new Uint8Array(buffer);
    let binaryString = '';
    for (let i = 0; i < bytes.length; i++) {
        binaryString += String.fromCharCode(bytes[i]!);
    }
    return btoa(binaryString);
}

/**
 * Encrypts plaintext using AES-256-GCM with the provided key and nonce.
 * @param keyBase64 - 32-byte AES-256 key encoded as Base64
 * @param plaintextBase64 - Data to encrypt encoded as Base64
 * @param nonceBase64 - 12-byte nonce/IV encoded as Base64
 * @returns Ciphertext with authentication tag appended, encoded as Base64
 */
export async function aesGcmEncrypt(
    keyBase64: string,
    plaintextBase64: string,
    nonceBase64: string
): Promise<string> {
    const keyBuffer = base64ToArrayBuffer(keyBase64);
    const plaintextBuffer = base64ToArrayBuffer(plaintextBase64);
    const nonceBuffer = base64ToArrayBuffer(nonceBase64);

    if (keyBuffer.byteLength !== 32) {
        throw new Error('AES-256 key must be 32 bytes');
    }
    if (nonceBuffer.byteLength !== 12) {
        throw new Error('AES-GCM nonce must be 12 bytes');
    }

    // Import the key
    const cryptoKey = await crypto.subtle.importKey(
        'raw',
        keyBuffer,
        { name: 'AES-GCM' },
        false,
        ['encrypt']
    );

    // Encrypt
    const ciphertextBuffer = await crypto.subtle.encrypt(
        { name: 'AES-GCM', iv: nonceBuffer },
        cryptoKey,
        plaintextBuffer
    );

    return arrayBufferToBase64(ciphertextBuffer);
}

/**
 * Decrypts ciphertext using AES-256-GCM with the provided key and nonce.
 * @param keyBase64 - 32-byte AES-256 key encoded as Base64
 * @param ciphertextBase64 - Encrypted data with auth tag, encoded as Base64
 * @param nonceBase64 - 12-byte nonce/IV used during encryption, encoded as Base64
 * @returns Decrypted plaintext encoded as Base64
 */
export async function aesGcmDecrypt(
    keyBase64: string,
    ciphertextBase64: string,
    nonceBase64: string
): Promise<string> {
    const keyBuffer = base64ToArrayBuffer(keyBase64);
    const ciphertextBuffer = base64ToArrayBuffer(ciphertextBase64);
    const nonceBuffer = base64ToArrayBuffer(nonceBase64);

    if (keyBuffer.byteLength !== 32) {
        throw new Error('AES-256 key must be 32 bytes');
    }
    if (nonceBuffer.byteLength !== 12) {
        throw new Error('AES-GCM nonce must be 12 bytes');
    }

    // Import the key
    const cryptoKey = await crypto.subtle.importKey(
        'raw',
        keyBuffer,
        { name: 'AES-GCM' },
        false,
        ['decrypt']
    );

    // Decrypt
    const plaintextBuffer = await crypto.subtle.decrypt(
        { name: 'AES-GCM', iv: nonceBuffer },
        cryptoKey,
        ciphertextBuffer
    );

    return arrayBufferToBase64(plaintextBuffer);
}
