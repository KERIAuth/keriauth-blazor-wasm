/**
 * Minimal navigator.credentials shim for WebAuthn PRF operations.
 * Provides thin wrapper around browser WebAuthn API for C# interop.
 */

/// <reference types="chrome-types" />

// Constants matching the C# WebauthnService expectations
const KERI_AUTH_EXTENSION_NAME = 'KERI Auth';
const CREDS_CREATE_RP: PublicKeyCredentialRpEntity = { name: KERI_AUTH_EXTENSION_NAME };
const CREDS_PUBKEY_PARAMS: PublicKeyCredentialParameters[] = [
    { alg: -8, type: 'public-key' },   // EdDSA
    { alg: -7, type: 'public-key' },   // ES256
    { alg: -257, type: 'public-key' }  // RS256
];
const CREDS_CREATE_TIMEOUT = 60000;
const CREDS_GET_TIMEOUT = 60000;

/**
 * Result of credential creation (registration).
 */
export interface CredentialCreationResult {
    /** Base64URL-encoded credential ID */
    credentialId: string;
    /** Array of transport types (e.g., ["usb", "internal"]) */
    transports: string[];
    /** Whether the authenticator supports PRF extension */
    prfEnabled: boolean;
    /** Whether a resident key (passkey) was created */
    residentKeyCreated: boolean;
    /** AAGUID of the authenticator (UUID format, e.g., "08987058-cadc-4b81-b6e1-30de50dcbe96") */
    aaguid: string;
}

/**
 * Result of credential assertion (authentication).
 */
export interface CredentialAssertionResult {
    /** Base64URL-encoded credential ID */
    credentialId: string;
    /** Base64-encoded PRF first result (32 bytes), or null if PRF not supported */
    prfOutputBase64: string | null;
}

/**
 * Options for credential creation, passed from C#.
 */
export interface CreateCredentialOptions {
    /** Existing credential IDs to exclude (Base64URL) */
    excludeCredentialIds: string[];
    /** Resident key requirement: "required" | "preferred" | "discouraged" */
    residentKey: ResidentKeyRequirement;
    /** Authenticator attachment: "platform" | "cross-platform" or null */
    authenticatorAttachment: AuthenticatorAttachment | null;
    /** User verification: "required" | "preferred" | "discouraged" */
    userVerification: UserVerificationRequirement;
    /** Attestation: "none" | "indirect" | "direct" | "enterprise" */
    attestation: AttestationConveyancePreference;
    /** Hints for authenticator selection */
    hints: string[];
    /** User ID bytes as Base64 */
    userIdBase64: string;
    /** User display name */
    userName: string;
    /** PRF salt as Base64 (derived from profile identifier) */
    prfSaltBase64: string;
}

/**
 * Options for credential assertion, passed from C#.
 */
export interface GetCredentialOptions {
    /** Allowed credential IDs (Base64URL) */
    allowCredentialIds: string[];
    /** Known transports for each credential (parallel array) */
    transportsPerCredential: string[][];
    /** User verification: "required" | "preferred" | "discouraged" */
    userVerification: UserVerificationRequirement;
    /** PRF salt as Base64 (derived from profile identifier) */
    prfSaltBase64: string;
}

/**
 * Helper: Convert ArrayBuffer to Base64URL string
 */
function arrayBufferToBase64Url(buffer: ArrayBuffer): string {
    const bytes = new Uint8Array(buffer);
    let binaryString = '';
    for (let i = 0; i < bytes.length; i++) {
        binaryString += String.fromCharCode(bytes[i]!);
    }
    const base64 = btoa(binaryString);
    return base64.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/**
 * Helper: Convert ArrayBuffer to standard Base64 string
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
 * Helper: Convert Base64URL string to ArrayBuffer
 */
function base64UrlToArrayBuffer(base64Url: string): ArrayBuffer {
    let base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
    while (base64.length % 4 !== 0) {
        base64 += '=';
    }
    const binaryString = atob(base64);
    const bytes = new Uint8Array(binaryString.length);
    for (let i = 0; i < binaryString.length; i++) {
        bytes[i] = binaryString.charCodeAt(i);
    }
    return bytes.buffer;
}

/**
 * Helper: Convert Base64 string to ArrayBuffer
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
 * Helper: Extract AAGUID from authenticator data.
 * AAGUID is located at bytes 37-52 (16 bytes) in the attestedCredentialData section.
 * Returns UUID format string (e.g., "08987058-cadc-4b81-b6e1-30de50dcbe96").
 *
 * AuthenticatorData structure:
 * - rpIdHash: 32 bytes (0-31)
 * - flags: 1 byte (32)
 * - signCount: 4 bytes (33-36)
 * - attestedCredentialData (if AT flag set):
 *   - aaguid: 16 bytes (37-52)
 *   - credentialIdLength: 2 bytes (53-54)
 *   - credentialId: variable
 *   - credentialPublicKey: variable (COSE format)
 */
function extractAaguidFromAuthenticatorData(authData: ArrayBuffer): string {
    const bytes = new Uint8Array(authData);

    console.log(`[navigatorCredentialsShim] extractAaguid - authData length: ${bytes.length} bytes`);

    if (bytes.length < 37) {
        console.log(`[navigatorCredentialsShim] extractAaguid - authData too short for flags check`);
        return '00000000-0000-0000-0000-000000000000';
    }

    // Check if we have attested credential data (flags byte at position 32, bit 6 = 0x40)
    const flags = bytes[32]!;
    const hasAttestedCredentialData = (flags & 0x40) !== 0;
    const hasExtensions = (flags & 0x80) !== 0;

    console.log(`[navigatorCredentialsShim] extractAaguid - flags: 0x${flags.toString(16)}, ` +
        `AT=${hasAttestedCredentialData}, ED=${hasExtensions}`);

    if (!hasAttestedCredentialData) {
        console.log(`[navigatorCredentialsShim] extractAaguid - AT flag not set, no attested credential data`);
        return '00000000-0000-0000-0000-000000000000';
    }

    if (bytes.length < 55) {
        console.log(`[navigatorCredentialsShim] extractAaguid - authData too short for AAGUID (need 55, have ${bytes.length})`);
        return '00000000-0000-0000-0000-000000000000';
    }

    // AAGUID starts at byte 37 (after rpIdHash[32] + flags[1] + signCount[4])
    const aaguidBytes = bytes.slice(37, 53);

    // Format as UUID: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
    const hex = Array.from(aaguidBytes)
        .map(b => b.toString(16).padStart(2, '0'))
        .join('');

    const aaguid = `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20, 32)}`;
    console.log(`[navigatorCredentialsShim] extractAaguid - extracted AAGUID: ${aaguid}`);

    return aaguid;
}

/**
 * Creates a new WebAuthn credential with PRF extension.
 * Called from C# via IJSRuntime.
 */
export async function createCredential(
    optionsJson: string
): Promise<CredentialCreationResult> {
    const options: CreateCredentialOptions = JSON.parse(optionsJson);

    // Build exclude credentials list
    const excludeCredentials: PublicKeyCredentialDescriptor[] = options.excludeCredentialIds.map(id => ({
        id: base64UrlToArrayBuffer(id),
        type: 'public-key' as const,
        transports: ['usb', 'nfc', 'ble', 'internal', 'hybrid'] as AuthenticatorTransport[]
    }));

    // Build authenticator selection criteria
    const authenticatorSelection: AuthenticatorSelectionCriteria = {
        residentKey: options.residentKey,
        userVerification: options.userVerification,
    };
    if (options.authenticatorAttachment) {
        authenticatorSelection.authenticatorAttachment = options.authenticatorAttachment;
    }

    // Build PRF extension with salt
    const prfSaltBuffer = base64ToArrayBuffer(options.prfSaltBase64);
    const extensions: AuthenticationExtensionsClientInputs = {
        prf: {
            eval: {
                first: prfSaltBuffer
            }
        },
        credProps: true
    };

    // Build user entity
    const userIdBuffer = base64ToArrayBuffer(options.userIdBase64);
    const user: PublicKeyCredentialUserEntity = {
        id: userIdBuffer,
        name: options.userName,
        displayName: options.userName
    };

    // Build creation options
    const publicKeyOptions: PublicKeyCredentialCreationOptions & { hints?: string[] } = {
        rp: CREDS_CREATE_RP,
        user,
        challenge: crypto.getRandomValues(new Uint8Array(32)),
        pubKeyCredParams: CREDS_PUBKEY_PARAMS,
        authenticatorSelection,
        excludeCredentials,
        extensions,
        timeout: CREDS_CREATE_TIMEOUT,
        attestation: options.attestation,
        hints: options.hints
    };

    // Call WebAuthn API
    const credential = await navigator.credentials.create({
        publicKey: publicKeyOptions
    }) as PublicKeyCredential | null;

    if (!credential) {
        throw new Error('navigator.credentials.create() returned null');
    }

    // Extract extension results
    const clientExtensionResults = credential.getClientExtensionResults() as AuthenticationExtensionsClientOutputs & {
        prf?: { enabled?: boolean };
        credProps?: { rk?: boolean };
    };

    const prfEnabled = clientExtensionResults.prf?.enabled === true;
    const residentKeyCreated = clientExtensionResults.credProps?.rk === true;

    // Extract credential ID, transports, and AAGUID
    const credentialId = arrayBufferToBase64Url(credential.rawId);
    const response = credential.response as AuthenticatorAttestationResponse;
    let transports: string[] = [];

    // Extract AAGUID from authenticator data
    // Try getAuthenticatorData() first (available in modern browsers)
    // Fall back to parsing attestationObject if not available
    let aaguid = '00000000-0000-0000-0000-000000000000';
    if (typeof response.getAuthenticatorData === 'function') {
        const authData = response.getAuthenticatorData();
        console.log(`[navigatorCredentialsShim] createCredential - using getAuthenticatorData()`);
        aaguid = extractAaguidFromAuthenticatorData(authData);
    } else {
        // Fallback: parse attestationObject (CBOR encoded)
        // The attestationObject contains authData which has the AAGUID
        console.log(`[navigatorCredentialsShim] createCredential - getAuthenticatorData() not available, trying attestationObject`);
        try {
            // attestationObject is CBOR-encoded, authData is at a known offset after the "authData" key
            // For simplicity, we'll try to find the authData within the attestationObject
            const attestationObject = new Uint8Array(response.attestationObject);
            // The authData in CBOR is usually after the key "authData" (0x68 0x61 0x75 0x74 0x68 0x44 0x61 0x74 0x61)
            // This is a simplified approach - look for the pattern and extract
            const authDataMarker = [0x68, 0x61, 0x75, 0x74, 0x68, 0x44, 0x61, 0x74, 0x61]; // "authData" in CBOR text
            let markerIndex = -1;
            for (let i = 0; i < attestationObject.length - authDataMarker.length; i++) {
                let match = true;
                for (let j = 0; j < authDataMarker.length; j++) {
                    if (attestationObject[i + j] !== authDataMarker[j]) {
                        match = false;
                        break;
                    }
                }
                if (match) {
                    markerIndex = i;
                    break;
                }
            }
            if (markerIndex >= 0) {
                // After "authData" key, there's a CBOR byte string header
                // Skip the key (9 bytes) and parse the byte string length
                const headerStart = markerIndex + 9;
                const header = attestationObject[headerStart];
                let authDataStart = headerStart + 1;
                let authDataLength = 0;

                if (header !== undefined) {
                    if ((header & 0xe0) === 0x40) {
                        // Short byte string (0x40-0x57)
                        authDataLength = header & 0x1f;
                    } else if (header === 0x58) {
                        // 1-byte length
                        authDataLength = attestationObject[headerStart + 1] || 0;
                        authDataStart = headerStart + 2;
                    } else if (header === 0x59) {
                        // 2-byte length
                        authDataLength = ((attestationObject[headerStart + 1] || 0) << 8) | (attestationObject[headerStart + 2] || 0);
                        authDataStart = headerStart + 3;
                    }
                }

                if (authDataLength > 0 && authDataStart + authDataLength <= attestationObject.length) {
                    const authData = attestationObject.slice(authDataStart, authDataStart + authDataLength);
                    aaguid = extractAaguidFromAuthenticatorData(authData.buffer);
                }
            }
        } catch (e) {
            console.warn(`[navigatorCredentialsShim] createCredential - failed to parse attestationObject:`, e);
        }
    }
    console.log(`[navigatorCredentialsShim] createCredential - AAGUID: ${aaguid}`);

    if (typeof response.getTransports === 'function') {
        transports = response.getTransports() || [];
    }

    // Log what the browser returned for transports
    console.log(
        `[navigatorCredentialsShim] createCredential - getTransports() returned: [${transports.join(', ')}], ` +
        `authenticatorAttachment requested: ${options.authenticatorAttachment ?? '(none/null)'}`
    );

    // Fallback transport inference if getTransports() returns empty
    if (transports.length === 0) {
        let fallbackReason: string;
        if (options.authenticatorAttachment === 'platform') {
            transports = ['internal'];
            fallbackReason = 'platform attachment';
        } else if (options.authenticatorAttachment === 'cross-platform') {
            transports = ['usb', 'nfc', 'ble', 'hybrid'];
            fallbackReason = 'cross-platform attachment';
        } else {
            transports = ['usb', 'nfc', 'ble', 'internal', 'hybrid'];
            fallbackReason = 'no attachment specified (includes all transports)';
        }
        console.warn(
            `[navigatorCredentialsShim] createCredential - Using fallback transports: [${transports.join(', ')}] ` +
            `(reason: ${fallbackReason})`
        );
    }

    return {
        credentialId,
        transports,
        prfEnabled,
        residentKeyCreated,
        aaguid
    };
}

/**
 * Gets an assertion from a WebAuthn credential with PRF extension.
 * Called from C# via IJSRuntime.
 */
export async function getCredential(
    optionsJson: string
): Promise<CredentialAssertionResult> {
    const options: GetCredentialOptions = JSON.parse(optionsJson);

    // Build allow credentials list with per-credential transports
    const allowCredentials: PublicKeyCredentialDescriptor[] = options.allowCredentialIds.map((id, index) => {
        const transports = options.transportsPerCredential[index] || ['usb', 'nfc', 'ble', 'internal', 'hybrid'];
        return {
            id: base64UrlToArrayBuffer(id),
            type: 'public-key' as const,
            transports: transports as AuthenticatorTransport[]
        };
    });

    // Build PRF extension with salt
    const prfSaltBuffer = base64ToArrayBuffer(options.prfSaltBase64);
    const extensions: AuthenticationExtensionsClientInputs = {
        prf: {
            eval: {
                first: prfSaltBuffer
            }
        } as AuthenticationExtensionsPRFInputs
    };

    // Build request options
    const publicKeyOptions: PublicKeyCredentialRequestOptions = {
        challenge: crypto.getRandomValues(new Uint8Array(32)),
        allowCredentials,
        userVerification: options.userVerification,
        extensions,
        timeout: CREDS_GET_TIMEOUT
    };

    // Call WebAuthn API
    const assertion = await navigator.credentials.get({
        publicKey: publicKeyOptions
    }) as PublicKeyCredential | null;

    if (!assertion) {
        throw new Error('navigator.credentials.get() returned null');
    }

    // Extract credential ID
    const credentialId = arrayBufferToBase64Url(assertion.rawId);

    // Extract PRF output
    const extensionResults = assertion.getClientExtensionResults() as AuthenticationExtensionsClientOutputs & {
        prf?: { results?: { first?: ArrayBuffer } };
    };

    let prfOutputBase64: string | null = null;
    if (extensionResults.prf?.results?.first) {
        prfOutputBase64 = arrayBufferToBase64(extensionResults.prf.results.first);
    }

    return {
        credentialId,
        prfOutputBase64
    };
}
