/// <reference types="chrome" />

interface User extends PublicKeyCredentialUserEntity {
    id: Uint8Array;
    name: string;
    displayName: string; // TODO: required?
}

enum ErrorCode {
    VALIDATION_ERROR = "VALIDATION_ERROR",
    UNSUPPORTED_FEATURE = "UNSUPPORTED_FEATURE",
    CREDENTIAL_ERROR = "CREDENTIAL_ERROR",
    TIMEOUT_ERROR = "TIMEOUT_ERROR",
    UNKNOWN_ERROR = "UNKNOWN_ERROR",
}

// TODO P2 move this to shared utility typescript file
export type FluentResult<T> = {
    isSuccess: boolean;
    errors: string[];
    value?: T;
};

// Constant fixed properties
export const KERI_AUTH_EXTENSION_NAME = "KERI Auth";
export const CREDS_CREATE_RP: PublicKeyCredentialRpEntity = { name: KERI_AUTH_EXTENSION_NAME }; // Note that id is intentionally left off!  See See https://chromium.googlesource.com/chromium/src/+/main/content/browser/webauth/origins.md
export const CREDS_CREATE_ATTESTATION = "none"; // TODO P3: "direct" ensures software receives information about the authenticator's hardware to verify its security
export const CREDS_PUBKEY_PARAMS: PublicKeyCredentialParameters[] = [
    { alg: -7, type: "public-key" },   // ES256
    { alg: -257, type: "public-key" }  // RS256
];
export const CREDS_CREATE_TIMEOUT = 60000;
export const CREDS_GET_TIMEOUT = 60000;
export const CREDS_CREATE_AUTHENTICATOR_SELECTION: AuthenticatorSelectionCriteria = {
    // TODO P2 Set these authenticator selection criteria to strongest levels, then allow user to lower the requirements in preferences.
    residentKey: "required", // preferred, or required for more safety
    userVerification: "required", // preferred or required. Enforce user verification (e.g., biometric, PIN)
    authenticatorAttachment: "cross-platform", // note that "platform" is stronger iff it supports PRF. TODO P2 could make this a user preference
    "requireResidentKey": true,           // True for passwordless and hardware-backed credentials
};
export const ENCRYPT_KEY_LABEL = "KERI Auth";
export const ENCRYPT_KEY_INFO = new TextEncoder().encode(ENCRYPT_KEY_LABEL);
export const ENCRYPT_DERIVE_KEY_TYPE = { name: "AES-GCM", length: 256 };
export const ENCRYPT_NON_SECRET_NOUNCE = new Uint8Array([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
export const ENCRYPT_KDA_SALT = new Uint8Array(0); // salt is a required argument for `deriveKey()`, but can be empty
export const ENCRYPT_DERIVE_KEY_ALGO = { name: "HKDF", info: ENCRYPT_KEY_INFO, salt: ENCRYPT_KDA_SALT, hash: "SHA-256" };

/*
 * Helper function to compare two Uint8Arrays
 */
function arraysEqual(a: Uint8Array, b: Uint8Array): boolean {
    if (a.length !== b.length) return false;
    for (let i = 0; i < a.length; i++) {
        if (a[i] !== b[i]) return false;
    }
    return true;
}

/*
 *
 */
const getExtensions = (firstSalt: Uint8Array): any => {
    return {
        "prf": {
            "eval": {
                "first": firstSalt,
            }
        }
    };
}

/**
 * Checks for WebAuthn support, available only when running in a secure context
 */
export const isWebauthnSupported = (): boolean => !!self.PublicKeyCredential;

/**
 * Helper function to retry an asynchronous operation with a timeout.
 */
async function retry<T>(operation: () => Promise<T>, retries: number, timeout: number): Promise<T> {
    for (let attempt = 1; attempt <= retries; attempt++) {
        const result = await Promise.race([operation(), new Promise<never>((_, reject) => setTimeout(() => reject(new Error(ErrorCode.TIMEOUT_ERROR)), timeout))]);
        if (result) return result as T;
    }
    throw new Error("Max retries reached.");
}


/*
 * Because chrome.storage.sync is specific per browser profile and per-extension, this is a secret identifier will be unique to each profile.
 * This method essentially "fingerprints" a profile.
 */
export async function getProfileIdentifier(): Promise<string> {
    return new Promise((resolve) => {
        chrome.storage.sync.get(['profileIdentifier'], (data) => {
            if (data.profileIdentifier) {
                resolve(data.profileIdentifier);
            } else {
                const newIdentifier = crypto.randomUUID();
                chrome.storage.sync.set({ profileIdentifier: newIdentifier });
                resolve(newIdentifier);
            }
        });
    });
}

/*
 *
 */
async function generateChallenge(extensionId: string, profileIdentifier: string): Promise<Uint8Array> {
    const concatenatedInput = `${extensionId}:${profileIdentifier}`;
    const hash = crypto.subtle.digest("SHA-256", new TextEncoder().encode(concatenatedInput));
    return new Uint8Array(await hash);
}

/*
 * Get a UserId based on the randomly generated browser profile ID
 */
async function getOrCreateUserId(): Promise<Uint8Array> {
    let profileIdentifier: string;
    try {
        profileIdentifier = await getProfileIdentifier();
        if (!profileIdentifier) {
            throw new Error("Profile identifier cannot be empty.");
        }
    } catch (error) {
        console.error("Error fetching profile identifier:", error);
        throw error;
    }
    return hashStringToUint8Array(profileIdentifier);
}

/*
 *
 */
async function hashStringToUint8Array(input: string): Promise<Uint8Array> {
    // TODO P1, why not simply use the following:
    // const hash = crypto.subtle.digest("SHA-256", new TextEncoder().encode(concatenatedInput));

    // Encode the input string to a Uint8Array
    const encoder = new TextEncoder();
    const data = encoder.encode(input);

    // Use Web Crypto API to hash the data with SHA-256
    const hashBuffer = await crypto.subtle.digest("SHA-256", data);

    // Convert the hash buffer to a Uint8Array
    const hashArray = new Uint8Array(hashBuffer);

    // Return the 32-byte Uint8Array result
    return hashArray;
}

/*
 *
 */
async function getOrCreateUser(): Promise<User> {
    let id = await getOrCreateUserId();
    let user: User;
    const createDateString = new Date().toISOString();
    user = {
        id: id,
        name: `${KERI_AUTH_EXTENSION_NAME}     ${createDateString}`,
        displayName: `${KERI_AUTH_EXTENSION_NAME}     ${createDateString}`
    };
    return user;
}

/*
 *  Get array of excluded credentials in order to prevent re-registration of the same authenticator to ensure uniqueness
 */
function getExcludeCredentialsFromCreate(credentialIds: string[], transports: string[]): PublicKeyCredentialDescriptor[] {
    const pkcds = [] as PublicKeyCredentialDescriptor[];
    for (const credentialId in credentialIds) {
        const encoder = new TextEncoder();
        const credIdUint8Array = encoder.encode(credentialId);
        const pkcd = {
            id: credIdUint8Array,
            transports: transports as AuthenticatorTransport[],
            type: "public-key"
        } as PublicKeyCredentialDescriptor;
        pkcds.push(pkcd);
    }
    return pkcds;
}

/*
 * Transform the browser profileId into a salt
 */
async function derive32Uint8ArrayFromProfileId(): Promise<Uint8Array> {
    const profileIdentifier = await getProfileIdentifier();
    // transform guid into 32 byte salt
    const encoder = new TextEncoder();
    // Convert GUID string to Uint8Array
    const guidBytes = encoder.encode(profileIdentifier);
    const hash = await crypto.subtle.digest('SHA-256', guidBytes)
    return new Uint8Array(hash); // 32 bytes
}

// Note name and types/shape needs to align with definition in IWeebauthnService
export interface CredentialWithPRF {
    credentialID: string; // Base64Url-encoded Credential ID
    transports: string[]; // Array of transport types (e.g., "usb", "nfc", "ble", "internal")
}


/*
 * Register a credential with a user-chosen authenticator, restricting it from re-registering one of the previously stored credential. 
 * Returns the new credentialId or throws.
 */
export async function registerCredential(registeredCredIds : string[]): Promise<CredentialWithPRF> {
    try {
        const options: PublicKeyCredentialCreationOptions = {
            rp: CREDS_CREATE_RP,
            user: await getOrCreateUser(),
            challenge: crypto.getRandomValues(new Uint8Array(32)),
            pubKeyCredParams: CREDS_PUBKEY_PARAMS,
            authenticatorSelection: CREDS_CREATE_AUTHENTICATOR_SELECTION,
            excludeCredentials: getExcludeCredentialsFromCreate( registeredCredIds, ["usb", "nfc", "ble", "internal"] as AuthenticatorTransport[]), // TODO P2 use the actual remembered transports
            extensions: getExtensions(await derive32Uint8ArrayFromProfileId()),
            timeout: CREDS_CREATE_TIMEOUT,
            attestation: CREDS_CREATE_ATTESTATION
        };

        const credential: PublicKeyCredential | null = await navigator.credentials.create({
            publicKey: options
        }) as PublicKeyCredential;

        if (credential == null) {
            throw new Error("credentials.create() returned null or timed out");
        }

        // Extract credential ID and transport
        const credentialID = toBase64Url(credential.rawId);
        // console.warn("registerCredential: credentialId ArrayBuffer: ", credential.rawId);
        // console.warn("registerCredential: credentialId Uint8Array: ", new Uint8Array(credential.rawId));
        // console.warn("registerCredential: credentialId base64Url: ", credentialID);

        const transports = (credential.response as AuthenticatorAttestationResponse).getTransports?.(); //  ?? [];
        // console.warn("Supported transports: ", transports);

        // Return a JSON-serializable object friendly for JSInterop
        return {
            credentialID, // Base64-encoded credential ID
            transports,   // Potentially empty array of transport types (e.g., "usb", "nfc", "ble", "internal")
        } as CredentialWithPRF;

    } catch (error) {
        console.warn("registerCredential error: ", error);
        throw error;
    }
}


function base64UrlToArrayBuffer(base64Url: string): ArrayBuffer {
    // Normalize the Base64Url string to Base64
    let base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');

    // Pad the Base64 string with '=' to make its length a multiple of 4
    while (base64.length % 4 !== 0) {
        base64 += '=';
    }

    // Decode Base64 to binary string
    const binaryString = atob(base64);

    // Create an ArrayBuffer and populate it with binary data
    const len = binaryString.length;
    const buffer = new ArrayBuffer(len);
    const uint8Array = new Uint8Array(buffer);
    for (let i = 0; i < len; i++) {
        uint8Array[i] = binaryString.charCodeAt(i);
    }

    return buffer;
}

/*
*
*/
function toBase64Url(buffer: ArrayBuffer): string {
    const base64 = btoa(String.fromCharCode(...new Uint8Array(buffer)));
    // Convert Base64 to Base64-URL
    return base64.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}


/*
 *  convert a base64 string to a Uint8Array
 */
function base64UrlToUint8Array(base64Url: string): Uint8Array {
    // Replace Base64-URL specific characters to standard Base64 characters
    const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');

    // Calculate the required padding (Base64 strings must be a multiple of 4 in length)
    const paddingNeeded = (4 - (base64.length % 4)) % 4;

    // Append '=' characters to make the string length a multiple of 4
    const paddedBase64 = base64 + (paddingNeeded ? '='.repeat(paddingNeeded) : '');

    // Decode the Base64 string to binary
    const binary = atob(paddedBase64);

    // Convert binary string to Uint8Array
    const len = binary.length;
    const bytes = new Uint8Array(len);
    for (let i = 0; i < len; i++) {
        bytes[i] = binary.charCodeAt(i);
    }
    return bytes;
}



/*
 *
 */
export async function authenticateCredential(credentialIdBase64s: string[]): Promise<AuthenticateCredResult> {
    try {
        // construct array of allowed credentials
        const allowCredentials: PublicKeyCredentialDescriptor[] = [];
        const transports: AuthenticatorTransport[] = ["usb", "nfc", "ble", "internal"]; // TODO P2 should use the same transports as the union of all saved credential transports

        // console.warn("credentialIdBase64s: ", credentialIdBase64s);
        for (const credentialIdBase64Url of credentialIdBase64s) {
            // console.warn("credentialIdBase64Url: ", credentialIdBase64Url);
            const credentialId = base64UrlToArrayBuffer(credentialIdBase64Url);
            // console.warn("credentialId ArrayBuffer: ", credentialId);
            allowCredentials.push({
                id: credentialId,
                type: "public-key",
                transports: transports as AuthenticatorTransport[],
            } as PublicKeyCredentialDescriptor);
        }
        console.warn("authenticateCredential: allowCredentials: ", allowCredentials);

        // Prepare PublicKeyCredentialRequestOptions
        const options: PublicKeyCredentialRequestOptions & { extensions: { prf: any } } = {
            challenge: crypto.getRandomValues(new Uint8Array(32)),
            allowCredentials: allowCredentials,
            // rpId and user are intentionally blank, since authenticator will have this stored based on credentialId
            timeout: CREDS_GET_TIMEOUT,
            userVerification: "required",
            extensions: {
                prf: {
                    eval: {
                        first: await derive32Uint8ArrayFromProfileId(),
                    },
                },
            },
        };

        // Call WebAuthn API to get assertion from authenticator
        const assertion: PublicKeyCredential | null = await navigator.credentials.get({ publicKey: options }) as PublicKeyCredential;

        if (!assertion) {
            console.error("Did not get assertion from authenticator. May have timed out.");
            throw Error("Did not get assertion from authenticator. May have timed out.")
        }

        const extensionResults = assertion.getClientExtensionResults();

        if (!((extensionResults as any).prf?.results?.first)) {
            console.log("This authenticator is not supported. Did not return PRF results.");
            throw Error("This authenticator is not supported. Did not return PRF results.");
        }

        // Import the input key material generated by assertion
        const keyData = new Uint8Array(
            (extensionResults as any).prf.results.first,
        ) as BufferSource;

        const keyDerivationKey = await crypto.subtle.importKey(
            "raw",
            keyData,
            "HKDF",
            false,
            ["deriveKey"],
        );

        // Derive the encryption key
        const encryptionKey = await crypto.subtle.deriveKey(
            ENCRYPT_DERIVE_KEY_ALGO,
            keyDerivationKey,
            ENCRYPT_DERIVE_KEY_TYPE,
            true,
            ["encrypt", "decrypt"],
        );

        const Uint8ArrayEncryptKey = await exportCryptoKeyToUint8Array(encryptionKey);


        // Convert Uint8Array to Base64 string
        const encryptKey = btoa(String.fromCharCode(...Uint8ArrayEncryptKey));
        const credentialIdbase64Url = toBase64Url(assertion.rawId);
        return { credentialId: credentialIdbase64Url, encryptKey: encryptKey } as AuthenticateCredResult;


    } catch (error) {
        console.error("authenticateCredential threw error: ", error);
        throw error;
    }
}


// Note name and types/shape needs to align with definition in IWeebauthnService
export interface AuthenticateCredResult {
    credentialId: string; // Base64Url-encoded Credential ID
    encryptKey: string; // Base64 of encrypt key
}


/*
 *
 *
// TODO P2 move into unit tests
// Encrypt message
const stringToEncrypt = "hello!";
const encrypted = await encryptWithNounce(encryptionKey, new TextEncoder().encode(stringToEncrypt));

// Decrypt message
const decrypted = await decryptWithNounce(encryptionKey, encrypted);
const decryptedString = (new TextDecoder()).decode(decrypted);

if (stringToEncrypt != decryptedString) {
    throw Error("encryption-decryption mismatch!");
} else {
    console.log(decryptedString);
}
*/

/*
 *
 */
async function exportCryptoKeyToUint8Array(key: CryptoKey): Promise<Uint8Array> {
    const exported = await crypto.subtle.exportKey("raw", key);
    return new Uint8Array(exported); // Convert ArrayBuffer to Uint8Array
}

/*
 *
 */
export const encryptWithNounce = async (
    encryptionKeyBase64: string, // Encryption key as a Base64 string
    dataStr: string           // Data to encrypt as a string
): Promise<string> => {
    // Decode the Base64 string into a Uint8Array
    const keyBytes = Uint8Array.from(atob(encryptionKeyBase64), c => c.charCodeAt(0));

    if (keyBytes.length !== 16 && keyBytes.length !== 32) {
        throw new Error("Encryption key must be exactly 16 or 32 bytes when encoded.");
    }

    // Convert the data string to a Uint8Array
    const data = new TextEncoder().encode(dataStr);

    // Generate a random IV (12 bytes is recommended for AES-GCM)
    const iv = crypto.getRandomValues(new Uint8Array(12));

    // Import the encryption key
    const encryptionKey = await crypto.subtle.importKey(
        "raw",
        keyBytes,
        { name: "AES-GCM" },
        false,
        ["encrypt"]
    );

    // Perform the encryption
    const algorithm = getEncryptionAlgorithm(ENCRYPT_NON_SECRET_NOUNCE);
    // console.log("algorithm:", algorithm);
    const encryptedArrayBuffer = await crypto.subtle.encrypt(
        algorithm,
        encryptionKey,
        data
    );

    // Convert the ArrayBuffer to a Base64 string
    const encryptedBytes = new Uint8Array(encryptedArrayBuffer);
    const base64Encrypted = btoa(String.fromCharCode(...encryptedBytes));

    return base64Encrypted;
};



/*
 *
 */
export const decryptWithNounce = async (
    encryptionKeyBase64: string, // Base64 string for the encryption key
    encryptedBase64: string      // Base64 string for the encrypted data
): Promise<string> => {

    // console.warn("encryptionKeyBase64: ", encryptionKeyBase64);
    // console.warn("encryptedBase64: ", encryptedBase64);

    // Decode the Base64 key into a Uint8Array
    const keyBytes = Uint8Array.from(atob(encryptionKeyBase64), c => c.charCodeAt(0));

    // console.log("Key byte length:", keyBytes.length);
    if (keyBytes.length !== 16 && keyBytes.length !== 32) {
        throw new Error("Encryption key must be 16 or 32 bytes.");
    }



    // Import the CryptoKey
    const encryptionKey = await crypto.subtle.importKey(
        "raw",
        keyBytes,
        { name: "AES-GCM" },
        true,
        ["decrypt", "encrypt"]
    );

    // Decode the Base64 encrypted data into a Uint8Array
    const encryptedBytes = Uint8Array.from(atob(encryptedBase64), c => c.charCodeAt(0));
    console.log("Encrypted data byte length:", encryptedBytes.length);

    // Decrypt the data
    const algorithm = getEncryptionAlgorithm(ENCRYPT_NON_SECRET_NOUNCE);
    console.log("Decryption algorithm:", algorithm);
    try {
        const decryptedArrayBuffer = await crypto.subtle.decrypt(
            algorithm,
            encryptionKey,
            encryptedBytes
        );
        // Convert the decrypted ArrayBuffer into a string
        const decryptedText = (new TextDecoder()).decode(decryptedArrayBuffer);
        return decryptedText;
    } catch (error) {
        console.log("could not decrypt and decode");
        throw (error);
    }
};

/*
 *
 */
const getEncryptionAlgorithm = (nounce: Uint8Array): AlgorithmIdentifier => {
    return { name: "AES-GCM", iv: nounce } as AlgorithmIdentifier;
};

/*
 *
 */
const getDeriveKeyAlgorithm = (salt: Uint8Array): AlgorithmIdentifier => {
    const deriveKeyAlgorithm = { name: "HKDF", ENCRYPT_KEY_INFO, salt, hash: "SHA-256" };
    return deriveKeyAlgorithm;
};