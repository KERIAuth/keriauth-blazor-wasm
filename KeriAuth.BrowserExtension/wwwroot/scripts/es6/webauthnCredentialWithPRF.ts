/// <reference types="chrome" />

import { IRegisteredAuthenticators } from "./IRegisteredAuthenticators.js"
import { IRegisteredAuthenticator } from "./IRegisteredAuthenticator.js"

interface StoredCredential {
    id: Uint8Array;
    name?: string; // Optional, e.g., to encryptionKeyLabel credentials for a user
}

interface User extends PublicKeyCredentialUserEntity {
    id: Uint8Array;
    name: string;
    displayName: string; // TODO: required?
}

type PublicKeyCredentialCreationOptionsWithPRF = PublicKeyCredentialCreationOptions & {
    extensions?: {
        "hmac-secret"?: boolean;
        prf: true | {
            eval?: { first: Uint8Array },
            evalContext?: ArrayBuffer[];
        },
    };
};
enum ErrorCode {
    VALIDATION_ERROR = "VALIDATION_ERROR",
    UNSUPPORTED_FEATURE = "UNSUPPORTED_FEATURE",
    CREDENTIAL_ERROR = "CREDENTIAL_ERROR",
    TIMEOUT_ERROR = "TIMEOUT_ERROR",
    UNKNOWN_ERROR = "UNKNOWN_ERROR",
}

interface ResultError {
    code: ErrorCode;
    message: string;
}

type Result<T> = { ok: true; value: T } | { ok: false; error: ResultError };

const nonSecretNounce = new Uint8Array([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
const credentialsCreateAttestation = "none"; // TODO P3: "direct" ensures software receives information about the authenticator's hardware to verify its security
const KeriAuthExtensionId = "pniimiboklghaffelpegjpcgobgamkal"; // TODO P1 get the extensionId from chrome.runtime...
const KeriAuthExtensionName = "KERI Auth";
const displayName = "KERI Auth";
const rpForCreate: PublicKeyCredentialRpEntity = { name: KeriAuthExtensionName }; // Note that id is intentionally left off!  See See https://chromium.googlesource.com/chromium/src/+/main/content/browser/webauth/origins.md
const pubKeyCredParams: PublicKeyCredentialParameters[] = [
    { alg: -7, type: "public-key" },   // ES256
    { alg: -257, type: "public-key" }  // RS256
];
const credentialsCreateTimeout = 60000;
const authenticatorSelectionForCreate: AuthenticatorSelectionCriteria = {
    // residentKey: "preferred", // or required for more safety
    // userVerification: "required", // Enforce user verification (e.g., biometric, PIN)
    authenticatorAttachment: "cross-platform", // note that "platform" is stronger iff it supports PRF. TODO P2 could make this a user preference
    // "requireResidentKey": true,           // For passwordless and hardware-backed credentials
};
const encryptionKeyLabel = "encryption key";
const encryptionKeyInfo = new TextEncoder().encode(encryptionKeyLabel);


/*
 *
 */
async function saveCredential(credential: StoredCredential): Promise<void> {
    const storageKey = "credentials";

    // Retrieve the stored credentials
    const result = await chrome.storage.sync.get(storageKey);
    console.log("got storageKey: ", result);

    // Check if the key exists and has a value; if not, initialize it to an empty array
    if (!result[storageKey]) {
        await chrome.storage.sync.set({ [storageKey]: [] });

        // TODO P1 also set a fake credential in the newer record structure. Currently assumes only one is supported
        const newRA: IRegisteredAuthenticator = {
            name: "new RA",
            credential: "base64credential",
            registeredUtc: new Date().toISOString(),
            lastUpdatedUtc: new Date().toISOString()
        };
        const RAS: IRegisteredAuthenticators = { ["authenticators"]: [newRA] };
        await chrome.storage.sync.set(RAS);
    }

    // Safely retrieve the credentials, which is guaranteed to be an array at this point
    let existingCredentials: StoredCredential[] = result[storageKey] || [];

    // Ensure the credential is JSON-serializable
    const sanitizedCredential = JSON.parse(JSON.stringify(credential));

    // Add the sanitized credential to the existing credentials
    existingCredentials.push(sanitizedCredential);

    console.log("Storing credential(s): 4", sanitizedCredential, existingCredentials);

    // Ensure the entire array is JSON-serializable
    existingCredentials = existingCredentials.map((cred) => JSON.parse(JSON.stringify(cred)));

    // Save the updated credentials back to storage
    await chrome.storage.sync.set({ [storageKey]: existingCredentials });

    console.log("Storing credential(s): 5: ", existingCredentials);
}


/*
 * Compare an assertion credential ID against stored credentials
 */
async function isCredentialIdStored(assertionCredentialId: Uint8Array): Promise<boolean> {
    const credentials = await loadCredentials();
    console.log("isCredentialIdStored: stored credentials:", credentials);
    console.log("isCredentialIdStored: assertionCredentialId:", assertionCredentialId);
    return credentials.some(cred => arraysEqual(cred.id, assertionCredentialId));
}


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
 * Load all credentials from chrome.storage.sync
 */
async function loadCredentials(): Promise<StoredCredential[]> {
    const result = await chrome.storage.sync.get("credentials");
    console.warn("loadCredentials get: ", result);
    return result.credentials || [];
}


/*
 *
 */
const getExtensions = (firstSalt: Uint8Array) /*: (AuthenticationExtensionsClientInputs & { "hmac-secret"?: boolean, "prf"?: any })*/ => {
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

/**
 * Creates a WebAuthn credential with PRF support, if available.
 * @param challenge The Uint8Array challenge.
 * @param rpId The relying party identifier.
 * @param user The user details.
 * @param retries Number of retry attempts for the operation.
 * @param debug Enable detailed debugging information.
 */
async function createCredentialWithPRF(
    challenge: Uint8Array,
    user: User,
    retries: number = 3,
    debug: boolean = false
): Promise<Result<PublicKeyCredential>> {

    if (!isWebauthnSupported()) {
        return {
            ok: false, error: { code: ErrorCode.UNSUPPORTED_FEATURE, message: "no PublicKeyCredential support" }
        };
    }

    // prepare parameters for credential creation
    const createDateString = new Date().toISOString();
    const user2: PublicKeyCredentialUserEntity = {
        id: user.id,
        name: `${user.name} 2 ${createDateString}`, //.split('T')[0];,  // TODO P1
        displayName: `${user.displayName} 2 created on ${createDateString}`,  // TODO P1
    }
    var challenge = crypto.getRandomValues(new Uint8Array(16)); // generateRandomBufferSource(16); //  crypto.getRandomValues( await generateChallenge(KeriAuthExtensionId);
    const excludeCredentials = await getExcludeCredentialsFromCreate();
    const credentialCreationOptions: PublicKeyCredentialCreationOptionsWithPRF = {
        challenge: challenge,
        rp: rpForCreate,
        user: user2,
        pubKeyCredParams: pubKeyCredParams,
        authenticatorSelection: authenticatorSelectionForCreate,
        extensions: getExtensions(firstSalt),
        // excludeCredentials: excludeCredentials,
        timeout: 60000 // 60 seconds
    };
    try {
        const credential: PublicKeyCredential | null = await retry(async () => {
            console.log("Attempting to create credential...");
            return await navigator.credentials.create({ publicKey: credentialCreationOptions }) as PublicKeyCredential;
        }, retries, 60000); // 60000ms timeout per attempt

        if (credential === null) {
            return {
                ok: false,
                error: {
                    code: ErrorCode.CREDENTIAL_ERROR,
                    message: "could not create credential"
                }
            }
        };

        console.log("credential: ", credential);

        const clientExtensionResults: AuthenticationExtensionsClientOutputs = (credential as PublicKeyCredential).getClientExtensionResults();
        if (clientExtensionResults) {
            console.log("Credential created... clientExtensionResults: ", clientExtensionResults);


            if (clientExtensionResults.hmacCreateSecret === null || clientExtensionResults.hmacCreateSecret) {
                console.log("PRF is supported, CTAP2.1 or higher.");
                return { ok: true, value: credential as PublicKeyCredential };
            } else {
                console.log("PRF not supported. Ensure the authenticator supports CTAP2.1.", debug);
                return {
                    ok: false,
                    error: {
                        code: ErrorCode.UNSUPPORTED_FEATURE,
                        message: "CTAP2.1 or higher is required for PRF support. Try updating the OS or using a compatible device."
                    }
                }
            }
        }
        else {
            return {
                ok: false,
                error: {
                    code: ErrorCode.UNKNOWN_ERROR,
                    message: "Failed to getClientExtensionResults."
                }
            }
        }
    } catch (error: unknown) {
        // Type checking for error to determine if it has a message property
        const errorMessage = error instanceof Error ? error.message : String(error);
        const errorCode = errorMessage === ErrorCode.TIMEOUT_ERROR ? ErrorCode.TIMEOUT_ERROR : ErrorCode.UNKNOWN_ERROR;
        return {
            ok: false,
            error: {
                code: errorCode,
                message: `Error during credential creation #10: ${errorMessage}`
            }
        };
    }
}

/*
 * Because chrome.storage.sync is specific per profile and per-extension this secret identifier will be unique to each profile.
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
function isError<T>(result: Result<T>): result is { ok: false; error: { code: ErrorCode; message: string } } {
    return result.ok === false;
}

/*
 *
 */
async function generateChallenge(extensionId: string): Promise<Uint8Array> {
    const profileIdentifier = await getProfileIdentifier();
    // TODO P3 pass in profileIdentifier as a param, so this is unit-testable
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
 * Derive a symmetric key from the PRF result
 * // TODO P0 rewrite deriveSymmetricKey
 */

async function deriveSymmetricKey(prfResult: ArrayBuffer, challenge: Uint8Array): Promise<CryptoKey> {
    const prfKeyMaterial = await crypto.subtle.importKey("raw", prfResult, "HKDF", false, ["deriveKey"]);
    return crypto.subtle.deriveKey(
        {
            name: "HKDF",
            salt: challenge,
            hash: "SHA-256",
        },
        prfKeyMaterial,
        { name: "AES-GCM", length: 256 },
        false,
        ["encrypt", "decrypt"]
    );
}

/*
 * Encrypt data with a symmetric key
 * // TODO P0 rewrite encryptData
 */
async function encryptData(data: string, key: CryptoKey): Promise<ArrayBuffer> {
    const iv = crypto.getRandomValues(new Uint8Array(12)); // 12-byte IV for AES-GCM
    const encodedData = new TextEncoder().encode(data);
    const encryptedData = await crypto.subtle.encrypt(
        { name: "AES-GCM", iv },
        key,
        encodedData
    );
    // Combine IV and ciphertext for storage
    return new Uint8Array([...iv, ...new Uint8Array(encryptedData)]).buffer;
}

/*
 * // Decrypt data with a symmetric key
 * // TODO P0 rewrite decryptData
 */
async function decryptData(encryptedData: ArrayBuffer, key: CryptoKey): Promise<string> {
    const encryptedBytes = new Uint8Array(encryptedData);
    const iv = encryptedBytes.slice(0, 12); // Extract the firstSalt 12 bytes as IV
    const ciphertext = encryptedBytes.slice(12); // Remaining bytes are the ciphertext
    const decryptedData = await crypto.subtle.decrypt(
        { name: "AES-GCM", iv },
        key,
        ciphertext
    );
    return new TextDecoder().decode(decryptedData);
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
        name: `${KeriAuthExtensionName}     ${createDateString}`,
        displayName: `${KeriAuthExtensionName}     ${createDateString}`
    };
    return user;
}

/*
 *
 */
function verifyClientExtensionResults(clientExtensionResults: any): void { // todo P0 what type?
    // TODO P1 should return a Result<X>
    if (!clientExtensionResults["hmac-secret"]) {
        throw new Error("hmac-secret is not supported by this authenticator.");
    }
    if (!clientExtensionResults["prf"]) {
        throw new Error("prf is not supported by this authenticator.");
    }
}

/*
 * // TODO P1, return an interface versus void. Change thrown errors to returns
 */
async function registerAndEncryptSecret(secret: string): Promise<void> {
    try {
        const challenge = await generateChallenge(KeriAuthExtensionId);
        var user = await getOrCreateUser();
        var excludeCredentials = await getExcludeCredentialsFromCreate();
        // TODO P0 can these options be in one place for the create options?
        // Define WebAuthn public key options
        const credentialCreationOptions: any = {  // note while this should be of type PublicKeyCredentialCreationOptions, that interface definition does not yet handles the "prf: true" section.
            challenge: challenge,
            rp: rpForCreate,
            user: user,
            pubKeyCredParams: pubKeyCredParams,
            timeout: credentialsCreateTimeout,
            authenticatorSelection: authenticatorSelectionForCreate,
            extensions: getExtensions(firstSalt),
            "attestation": credentialsCreateAttestation,
            "attestationFormats": [],
            "hints": [],
            // "excludeCredentials": excludeCredentials,
        };

        const credential = await navigator.credentials.create({ publicKey: credentialCreationOptions }) as PublicKeyCredential;
        if (!credential) {
            throw new Error("Credential creation failed #12: No credential returned.");
        }

        // Verify that hmac and PRF are supported
        const clientExtensionResults = (credential as any).getClientExtensionResults();

        verifyClientExtensionResults(clientExtensionResults);

        // Use the PRF result as part of the symmetric key derivation
        const prfResult = clientExtensionResults["hmac-secret"];
        const symmetricKey = await deriveSymmetricKey(prfResult, challenge);

        // Encrypt the secret using the derived symmetric key
        const encryptedSecret = await encryptData(secret, symmetricKey);

        // Store the encrypted secret in chrome.storage.local
        // Ensure `encryptedSecret` is stored as a Uint8Array, not as an ArrayBuffer
        chrome.storage.local.set({ encryptedSecret: new Uint8Array(encryptedSecret) }, () => {
            console.log("Encrypted secret stored successfully.");
        });

        // For testing, retrieve and decrypt the stored secret
        chrome.storage.local.get("encryptedSecret", (data: { [key: string]: any }) => {
            // Perform a runtime check to ensure data.encryptedSecret exists and is a Uint8Array
            if (data.encryptedSecret instanceof Uint8Array) {
                decryptData(data.encryptedSecret.buffer, symmetricKey)
                    .then((decryptedSecret) => {
                        console.log("Decrypted secret:", decryptedSecret);
                        if (secret != decryptedSecret) {
                            throw new Error("why?");
                        }
                    })
                    .catch((error) => {
                        console.error("Decryption failed:", error);
                    });
            } else {
                console.error("No valid encrypted secret found in storage.");
            }
        });
    } catch (error) {
        console.error("An error occurred:", error);
    }
}

/*
 *
 */
function coerceToArrayBuffer(thing: any, name?: any) {
    if (typeof thing === "string") {
        // base64url to base64
        thing = thing.replace(/-/g, "+").replace(/_/g, "/");

        // base64 to Uint8Array
        var str = window.atob(thing);
        var bytes = new Uint8Array(str.length);
        for (var i = 0; i < str.length; i++) {
            bytes[i] = str.charCodeAt(i);
        }
        thing = bytes;
    }

    // Array to Uint8Array
    if (Array.isArray(thing)) {
        thing = new Uint8Array(thing);
    }

    // Uint8Array to ArrayBuffer
    if (thing instanceof Uint8Array) {
        thing = thing.buffer;
    }

    // error if none of the above worked
    if (!(thing instanceof ArrayBuffer)) {
        throw new TypeError("could not coerce '" + name + "' to ArrayBuffer");
    }

    return thing;
};


/*
 *
 */
function hexToArrayBuffer(hex: string): ArrayBuffer {
    const bytes = new Uint8Array(hex.match(/.{1,2}/g)!.map(byte => parseInt(byte, 16)));
    return bytes.buffer;
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
export async function createCred() {
    /*
    var makeCredentialOptions = await fetchMakeCredentialOptions(data);

    console.log("Credential Options Object", makeCredentialOptions);

    if (makeCredentialOptions.status === "error") {
        console.log("Error creating credential options");
        console.log(makeCredentialOptions.errorMessage);
        return;
    }

    // Turn the challenge back into the accepted format of padded base64
    makeCredentialOptions.challenge = coerceToArrayBuffer(makeCredentialOptions.challenge);
    // Turn ID into a UInt8Array Buffer for some reason
    makeCredentialOptions.user.id = coerceToArrayBuffer(makeCredentialOptions.user.id);

    makeCredentialOptions.excludeCredentials = makeCredentialOptions.excludeCredentials.map((c) => {
        c.id = coerceToArrayBuffer(c.id);
        return c;
    });
    */

    //    var challenge2 = coerceToArrayBuffer("challenge", "challengeName");
    const challenge = await generateChallenge(KeriAuthExtensionId);
    var user = await getOrCreateUser();
    var excludeCredentials = await getExcludeCredentialsFromCreate();
    const credentialCreationOptions: any = {
        rp: rpForCreate,
        user: user,
        challenge: challenge,
        pubKeyCredParams: pubKeyCredParams,
        timeout: credentialsCreateTimeout,
        attestation: credentialsCreateAttestation,
        attestationFormats: [],
        authenticatorSelection: authenticatorSelectionForCreate,
        hints: [],
        // "excludeCredentials": excludeCredentials,
        extensions: getExtensions(firstSalt),
    }
    console.log("Credential Options Formatted", credentialCreationOptions);

    console.log("Creating PublicKeyCredential...");

    let newCredential; // : Credential | null;
    try {
        newCredential = await navigator.credentials.create({
            publicKey: credentialCreationOptions  // TODO P1 fix this usage in other .create()'s
        });
    } catch (e) {
        var msg = "Could not create credentials in browser. Probably because the username is already registered with your authenticator. Please change username or authenticator."
        console.error(msg, e);
    }
    console.log("PublicKeyCredential Created", newCredential);

    try {
        // TODO store credential (or remake it)
        // registerNewCredential(newCredential);

    } catch (e) {
        console.log(e);
    }
}

/**
 * This value is for sake of demonstration. Pick 32 random
 * bytes. `salt` can be static for your site or unique per
 * credential depending on your needs.
 */
const firstSalt: Uint8Array = crypto.getRandomValues(new Uint8Array(12)); // TODO P1: should be random on create, and then stored for subsequent calls?  P0 [1, 2, 3, 4]); See Matt's Headroom article

/*
 *
 */
export async function registerCredential(): Promise<void> {

    // Credential Creation Request
    const publicKey: any = { // PublicKeyCredentialCreationOptionsWithPRF = { // /*/ PublicKeyCredentialCreationOptions = {
        // Basic registration parameters
        rp: rpForCreate,
        user: await getOrCreateUser(),
        challenge: crypto.getRandomValues(new Uint8Array(32)),
        pubKeyCredParams: pubKeyCredParams,
        authenticatorSelection: authenticatorSelectionForCreate,
        extensions: getExtensions(firstSalt),
        timeout: credentialsCreateTimeout,
        attestation: "none"
    };

    let credential: PublicKeyCredential;
    try {
        credential = await navigator.credentials.create({
            publicKey
        }) as PublicKeyCredential;
        console.log("credential: ", credential);
    }
    catch (error) {
        console.error("An error occurred during credential creation:", error);
        return;
    }


    // Determine and store the credentialId
    const credentialId = btoa(String.fromCharCode(...new Uint8Array(credential.rawId)));
    console.log("Registered Credential ID:", credentialId);

    chrome.storage.sync.set({ credentialId }, () => {
        console.log("Stored Credential ID with PRF support.");
    });
}

/*
 *
 */
export const authenticateCredential = async (): Promise<void> => {
    // Retrieve the stored credentialId from chrome.storage.sync
    chrome.storage.sync.get("credentialId", async (result) => {
        const credentialIdBase64 = result.credentialId;
        if (!credentialIdBase64) {
            console.error("No credential ID found.");
            return;
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
            timeout: 60000,
            userVerification: "required",
            extensions: {
                prf: {
                    eval: {
                        first: firstSalt,
                    },
                },
            }
        };

        // Call WebAuthn API to get the credential assertion
        const assertion = await navigator.credentials.get({
            publicKey: options,
        }) as PublicKeyCredential;

        const extensionResults = assertion.getClientExtensionResults();
        console.log("auth1ExtensionResults: ", extensionResults);

        if (!((extensionResults as any).prf?.results?.first)) {
            console.log("This authenticator is not supported. Did not return PRF results.");
            return;
        }
        console.log("Good, this authenticator supports PRF.");

        // import the input key material
        const inputKeyMaterial = new Uint8Array(
            (extensionResults as any).prf.results.first,
        );


        // Import the input key material
        const keyDerivationKey = await crypto.subtle.importKey(
            "raw",
            inputKeyMaterial,
            "HKDF",
            false,
            ["deriveKey"],
        );

        // Derive the encryption key
        const derivedKeyType = { name: "AES-GCM", length: 256 };
        const kdaSalt = new Uint8Array(0); // salt is a required argument for `deriveKey()`, but can be empty
        const deriveKeyAlgorithm = { name: "HKDF", info: encryptionKeyInfo, salt: kdaSalt, hash: "SHA-256" };

        const encryptionKey = await crypto.subtle.deriveKey(
            deriveKeyAlgorithm,
            keyDerivationKey,
            derivedKeyType,
            false,  // should not be exportable, since we will re-derive this
            ["encrypt", "decrypt"],
        );

        // Encrypt message
        const encrypted = await crypto.subtle.encrypt(
            getEncryptionAlgorithm(nonSecretNounce),
            encryptionKey,
            new TextEncoder().encode("hello readers!"),
        );

        // Decrypt message
        const decrypted = await crypto.subtle.decrypt(
            getEncryptionAlgorithm(nonSecretNounce),
            encryptionKey,
            encrypted,
        );
        console.log((new TextDecoder()).decode(decrypted));
    });
};

/*
 *
 */
 export const decryptWithNounce = (encryptionKey: CryptoKey, encrypted: ArrayBuffer): Promise<ArrayBuffer> => {
    return crypto.subtle.decrypt(
        getEncryptionAlgorithm(nonSecretNounce),
        encryptionKey,
        encrypted,
    );
};

/*
 *
 */
const  getEncryptionAlgorithm = (nounce: Uint8Array): AlgorithmIdentifier => {
    return { name: "AES-GCM", iv: nounce } as AlgorithmIdentifier;
};

/*
 *
 */
const getDeriveKeyAlgorithm = (salt: Uint8Array): AlgorithmIdentifier => {
    const deriveKeyAlgorithm = { name: "HKDF", encryptionKeyInfo, salt, hash: "SHA-256" };
    return deriveKeyAlgorithm;
};