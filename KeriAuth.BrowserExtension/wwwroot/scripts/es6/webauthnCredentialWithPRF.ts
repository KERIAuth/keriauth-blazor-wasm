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
const KERI_AUTH_EXTENSION_NAME = "KERI Auth";
const CREDS_CREATE_RP: PublicKeyCredentialRpEntity = { name: KERI_AUTH_EXTENSION_NAME }; // Note that id is intentionally left off!  See See https://chromium.googlesource.com/chromium/src/+/main/content/browser/webauth/origins.md
const CREDS_CREATE_ATTESTATION = "none"; // TODO P3: "direct" ensures software receives information about the authenticator's hardware to verify its security
const CREDS_PUBKEY_PARAMS: PublicKeyCredentialParameters[] = [
    { alg: -7, type: "public-key" },   // ES256
    { alg: -257, type: "public-key" }  // RS256
];
const CREDS_CREATE_TIMEOUT = 60000;
const CREDS_GET_TIMEOUT = 60000;
const CREDS_CREATE_AUTHENTICATOR_SELECTION: AuthenticatorSelectionCriteria = {
    // TODO P2 Set these authenticator selection criteria to strongest levels, then allow user to lower the requirements in preferences.
    // residentKey: "preferred", // or required for more safety
    // userVerification: "required", // Enforce user verification (e.g., biometric, PIN)
    authenticatorAttachment: "cross-platform", // note that "platform" is stronger iff it supports PRF. TODO P2 could make this a user preference
    // "requireResidentKey": true,           // For passwordless and hardware-backed credentials
};
const ENCRYPT_KEY_LABEL = "asdf";
const ENCRYPT_KEY_INFO = new TextEncoder().encode(ENCRYPT_KEY_LABEL);
const ENCRYPT_DERIVE_KEY_TYPE = { name: "AES-GCM", length: 256 };
const ENCRYPT_NON_SECRET_NOUNCE = new Uint8Array([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
const ENCRYPT_KDA_SALT = new Uint8Array(0); // salt is a required argument for `deriveKey()`, but can be empty
const ENCRYPT_DERIVE_KEY_ALGO = { name: "HKDF", info: ENCRYPT_KEY_INFO, salt: ENCRYPT_KDA_SALT, hash: "SHA-256" };

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
 *
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
 *
 */
async function getExcludeCredentialsFromCreate() {
    // TODO P1 Prevent re-registration of the same authenticator to ensure uniqueness:
    // Prevent re-registration of the same authenticator to ensure uniqueness:
    return [{
        // type: "public-key",
        // id: "<existing-credential-id>"
        // transports: ["usb", "nfc", "ble", "internal"]
    }];
}

/*
 *
 */
async function derive32Uint8ArrayFromProfileId(): Promise<Uint8Array> {
    const profileIdentifier = await getProfileIdentifier();
    // transform guid into 32 byte salt
    const encoder = new TextEncoder();
    const guidBytes = encoder.encode(profileIdentifier); // Convert GUID string to Uint8Array
    const hash = await crypto.subtle.digest('SHA-256', guidBytes)
    return new Uint8Array(hash); // 32 bytes
}

/*
 *
 */
export async function registerCredentialJ(): Promise<string> {
    const options: any = { // PublicKeyCredentialCreationOptionsWithPRF = { // /*/ PublicKeyCredentialCreationOptions = {
        rp: CREDS_CREATE_RP,
        user: await getOrCreateUser(),
        challenge: crypto.getRandomValues(new Uint8Array(32)),
        pubKeyCredParams: CREDS_PUBKEY_PARAMS,
        authenticatorSelection: CREDS_CREATE_AUTHENTICATOR_SELECTION,
        extensions: getExtensions(await derive32Uint8ArrayFromProfileId()),
        timeout: CREDS_CREATE_TIMEOUT,
        attestation: CREDS_CREATE_ATTESTATION
    };

    let credential: PublicKeyCredential;
    try {
        credential = await navigator.credentials.create({
            publicKey: options
        }) as PublicKeyCredential;
        console.log("registerCredentialJ: credential: ", credential);
    }
    catch (error) {
        console.error("registerCredentialJ: An error occurred during credential creation:", error);
        const result = {
            isSuccess: false,
            errors: ["error occurred during credential creation"] //, error as string]
        } as FluentResult<void>;
        var ret = JSON.stringify(result);
        console.log("registerCredentialJ ret:", ret)
        return ret
    }

    if (!credential) {
        console.info("registerCredentialJ: no credential returned");
        const result = {
            isSuccess: false,
            errors: ["no credential returned from authenticator"],
        } as FluentResult<void>;
        var ret = JSON.stringify(result);
        console.log("registerCredentialJ ret:", ret)
        return ret; // TODO P0 include credential;
    }

    // TODO P1 remove the following after credential is returned
    // Determine credentialId and store it
    const credentialId = btoa(String.fromCharCode(...new Uint8Array(credential.rawId)));
    console.log("Registered Credential ID:", credentialId);
    chrome.storage.sync.set({ credentialId }, () => {
        console.log("Stored Credential ID with PRF support.");
    });

    console.info("registerCredentialJ: cred stored");
    const result = {
        isSuccess: true,
        errors: [],
    } as FluentResult<void>;
    var ret = JSON.stringify(result);
    console.log("registerCredentialJ ret:", ret)

    return ret; // TODO P0 include credential;
}

/*
 *
 */
export const authenticateCredential = async (): Promise<string> => {
    // Retrieve the stored credentialId from chrome.storage.sync
    const result = await chrome.storage.sync.get("credentialId");





    const credentialIdBase64 = result.credentialId;
    if (!credentialIdBase64) {
        console.error("No credential ID found.");
        return "no cred found";
    }

    const credentialId = Uint8Array.from(atob(credentialIdBase64), c => c.charCodeAt(0));

    // Prepare PublicKeyCredentialRequestOptions
    const options: any = { //  PublicKeyCredentialRequestOptions = {
        challenge: crypto.getRandomValues(new Uint8Array(32)),
        allowCredentials: [{
            id: credentialId,
            type: "public-key",
            transports: ["usb", "nfc", "ble", "internal"],  // TODO P2 should use the same transport as the saved credential (not just ID)
        }],
        // rpId is intentionally left blank
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
    const assertion = await navigator.credentials.get({
        publicKey: options,
    }) as PublicKeyCredential;

    const extensionResults = assertion.getClientExtensionResults();
    // console.log("auth1ExtensionResults: ", extensionResults);

    if (!((extensionResults as any).prf?.results?.first)) {
        console.log("This authenticator is not supported. Did not return PRF results.");
        // return "authenticator not supported";
    }

    // Import the input key material generated by assertion
    const keyData = new Uint8Array(
        (extensionResults as any).prf.results.first,
    );
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
        false,  // should not be exportable, since we will re-derive this
        ["encrypt", "decrypt"],
    );

    // TODO P2 test follow that can later be moved into unit tests
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

    // TODO P0
    return "end";
};

/*
 *
 */
// TODO P1 is this redundant with encrptData()?  DRY
export const encryptWithNounce = async (encryptionKey: CryptoKey, data: BufferSource): Promise<ArrayBuffer> => {

    return await crypto.subtle.encrypt(
        getEncryptionAlgorithm(ENCRYPT_NON_SECRET_NOUNCE),
        encryptionKey,
        data,
    );
}

/*
 *
 */
// TODO P1 is this redundant with decryptData()?  DRY
export const decryptWithNounce = (encryptionKey: CryptoKey, encrypted: ArrayBuffer): Promise<ArrayBuffer> => {
    return crypto.subtle.decrypt(
        getEncryptionAlgorithm(ENCRYPT_NON_SECRET_NOUNCE),
        encryptionKey,
        encrypted,
    );
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