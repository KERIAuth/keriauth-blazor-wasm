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

    // Extract credential ID and transports
    const credentialId = arrayBufferToBase64Url(credential.rawId);
    const response = credential.response as AuthenticatorAttestationResponse;
    let transports: string[] = [];

    if (typeof response.getTransports === 'function') {
        transports = response.getTransports() || [];
    }

    // Log what the browser returned for transports
    console.warn(
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
        residentKeyCreated
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
