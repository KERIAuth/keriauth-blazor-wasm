/// <reference types="chrome" />

interface User extends PublicKeyCredentialUserEntity {
    id: Uint8Array;
    name: string;
    displayName: string; // TODO: required?
}

type PublicKeyCredentialCreationOptionsWithPRF = PublicKeyCredentialCreationOptions & {
    extensions?: {
        "hmac-secret"?: boolean;
        prf?: true | {
            eval?: { first: Uint8Array }, // First input to PRF.  Not ArrayBuffer[]  ?
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

async function saveCredential(credential: StoredCredential): Promise<void> {
    const storageKey = "credentials";

    // Retrieve the stored credentials
    const result = await chrome.storage.sync.get(storageKey);
    console.log("got storageKey: ", result);

    // Check if the key exists and has a value; if not, initialize it to an empty array
    if (!result[storageKey]) {

        await chrome.storage.sync.set({ [storageKey]: [] });
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


interface StoredCredential {
    id: Uint8Array;
    name?: string; // Optional, e.g., to label credentials for a user
}

// Compare an assertion credential ID against stored credentials
async function isCredentialIdStored(assertionCredentialId: Uint8Array): Promise<boolean> {
    const credentials = await loadCredentials();
    console.log("isCredentialIdStored: stored credentials:", credentials);
    console.log("isCredentialIdStored: assertionCredentialId:", assertionCredentialId);




    return credentials.some(cred => arraysEqual(cred.id, assertionCredentialId));
}

// Helper function to compare two Uint8Arrays
function arraysEqual(a: Uint8Array, b: Uint8Array): boolean {
    if (a.length !== b.length) return false;
    for (let i = 0; i < a.length; i++) {
        if (a[i] !== b[i]) return false;
    }
    return true;
}

// Load all credentials from chrome.storage.sync
async function loadCredentials(): Promise<StoredCredential[]> {
    const result = await chrome.storage.sync.get("credentials");
    console.warn("loadCredentials get: ", result);
    return result.credentials || [];
}

const attestation = "none"; // TODO P3: "direct" ensures software receives information about the authenticator's hardware to verify its security

export async function createAndStoreCredential(): Promise<void> {
    var user = await getOrCreateUser();

    var rp2 = rpForCreate;
    rp2.id = KeriAuthExtensionId;
    var excludeCredentials = await getExcludeCredentialsFromCreate();
    const credentialCreationOptions: any = { // PublicKeyCredentialCreationOptions = {
        challenge: crypto.getRandomValues(new Uint8Array(32)),
        rp: rp2,
        user: user,
        pubKeyCredParams: pubKeyCredParams,
        extensions: extensions,
        // "excludeCredentials": excludeCredentials,
        authenticatorSelection: authenticatorSelection,
        timeout: makeCredentialTimeout,
        attestation: attestation
    };

    const credential = await navigator.credentials.create({ publicKey: credentialCreationOptions });
    if (credential && credential instanceof PublicKeyCredential) {
        const credentialId = new Uint8Array(credential.rawId);
        await saveCredential({
            id: credentialId,
            name: user.name // Same as what the user sees, with the date.
        });
        console.log("Credential stored successfully.");
    } else {
        console.error("Failed to create credential.");
    }


}

/*
export async function getAndVerifyAssertion(): Promise<boolean> {
    const storedCredentials = await loadCredentials();


    const allowCredentials = storedCredentials.map(cred => ({
        id: cred.id,
        type: "public-key",
        name: cred.name,
        // TODO P1 See PublicKeyCredentialRequestOptions https://developer.mozilla.org/en-US/docs/Web/API/PublicKeyCredentialRequestOptions#instance_properties
        transports: ["ble", "hybrid", "internal", "nfc", "usb"]
    }));

    // TODO P0 remove tests here
    const credentialId = new Uint8Array([123, 167, 117, 54, 155, 0, 118, 34, 185, 200, 99, 145, 176, 139, 130, 156, 31, 252, 2, 248, 210, 219, 54, 78, 210, 18, 51, 242, 91, 119, 4, 211]);
    const allowCredentials2 = [
        {
            id: credentialId.buffer, // Ensure `id` is an ArrayBuffer
            type: "public-key"
        }
    ];

    console.log("getAndVerifyAssertion allowedCredentials: ", allowCredentials);

    var challenge = await generateChallenge(KeriAuthExtensionName);

    const options: any = { // PublicKeyCredentialRequestOptions = {
        publicKey: {
            challenge: challenge, // crypto.getRandomValues(new Uint8Array(32)), // challenge
            allowCredentials2, // TODO P1.  See above
            extensions: extensions, // { prf: { inputs: { first: crypto.getRandomValues(new Uint8Array(32)) } } },
            hints: [
                "security-key",
                "client-device",
                "hybrid"
            ],
            // rpID: rpForCreate.id, //  ....   // TODO P2 define this for security purposes
            timeout: makeCredentialTimeout,
            userVerification: "preferred", // Options: "required", "preferred", "discouraged"
        }
    };

    let assertion: Credential | null;
    try {
        assertion = await navigator.credentials.get(options);
        console.log("got assertion: ", assertion);
    }
    catch (error: any) {
        // Handle different types of WebAuthn errors
        var msg: string;
        if (error.name === "NotAllowedError") {
            msg = "Authentication was not allowed or cancelled by the user.";
        } else if (error.name === "InvalidStateError") {
            msg = "The security key is not registered with this site.";
        } else {
            msg = "An unexpected error occurred: " + error.message;
        }
        console.warn(msg);
        return false;
    }
    if (assertion && assertion instanceof PublicKeyCredential) {
        const assertionId = new Uint8Array(assertion.rawId);
        console.log("assertionId: ", assertionId);
        const isStored = await isCredentialIdStored(assertionId);
        console.log(`Credential verification result: ${isStored}`);
        return isStored;
    } else {
        console.error("Failed to retrieve assertion.");
        return false;
    }
}
*/

type Result<T> = { ok: true; value: T } | { ok: false; error: ResultError };

// const's used widely in these functions...
const KeriAuthExtensionId = "pniimiboklghaffelpegjpcgobgamkal"; // TODO P1 get the extensionId from chrome.runtime...
const KeriAuthExtensionName = "KERI Auth";
const displayName = "KERI Auth displayName"; // TODO P2
const rpForCreate: PublicKeyCredentialRpEntity = { name: KeriAuthExtensionName }; // Note that id is intentionally left off!  See See https://chromium.googlesource.com/chromium/src/+/main/content/browser/webauth/origins.md
const pubKeyCredParams: PublicKeyCredentialParameters[] = [
    { alg: -7, type: "public-key" },   // ES256
    { alg: -257, type: "public-key" }  // RS256
];
const first = new Uint8Array(32); // TODO P1: should be random on create, and then stored for subsequent calls?  P0 [1, 2, 3, 4]);


const extensions = { // AuthenticationExtensionsClientInputs & { "hmac-secret"?: boolean, "prf"?: any } = {
    "hmac-secret": true,
    // "credProps": true, //TODO P1 needed?  Can't have set true when getting.
    "prf": {   // See https://w3c.github.io/webauthn/#dom-authenticationextensionsprfinputs-eval within https://w3c.github.io/webauthn/#prf-extension
        "eval": {
            "first": first, // generateChallenge("asdf"), //   new Uint8Array(32)
        }
    },
    // credProps: true,  // See https://developer.mozilla.org/en-US/docs/Web/API/Web_Authentication_API/WebAuthn_extensions
    // minPinLength: true
    // hmacCreateSecret: true,

};

const makeCredentialTimeout = 60000;
const authenticatorSelection: AuthenticatorSelectionCriteria = {
    // residentKey: "preferred", // or required for more safety
    // userVerification: "required", // Enforce user verification (e.g., biometric, PIN)
    authenticatorAttachment: "cross-platform", // note that "platform" is stronger iff it supports PRF. TODO P2 could make this a user preference
    // "requireResidentKey": true,           // For passwordless and hardware-backed credentials
};

/**
 * Checks for WebAuthn and PRF feature support.
 */
export function checkWebAuthnSupport(): boolean {
    if (self.PublicKeyCredential) {
        return true;
    }
    console.log("no PublicKeyCredential support");
    return false;
}

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

    if (!checkWebAuthnSupport()) {
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
        authenticatorSelection: authenticatorSelection,
        extensions: extensions,
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


// Because chrome.storage.sync is specific per profile and per-extension this secret identifier will be unique to each profile.
// This method essentially "fingerprints" a profile.
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

function isError<T>(result: Result<T>): result is { ok: false; error: { code: ErrorCode; message: string } } {
    return result.ok === false;
}

async function generateChallenge(extensionId: string): Promise<Uint8Array> {
    const profileIdentifier = await getProfileIdentifier();
    // TODO P3 pass in profileIdentifier as a param, so this is unit-testable
    const concatenatedInput = `${extensionId}:${profileIdentifier}`;
    const hash = crypto.subtle.digest("SHA-256", new TextEncoder().encode(concatenatedInput));
    return new Uint8Array(await hash);
}

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

// Example usage
export async function test(): Promise<String> {
    var user = await getOrCreateUser();
    const challenge = await generateChallenge(KeriAuthExtensionId);
    var result = await createCredentialWithPRF(challenge, user);
    if (isError(result)) {
        console.error("Credential creation failed #11: already exists?: ", result);
        return "Credential creation failed #11: already exists?" + result.error.message
    } else {
        console.log("Credential created:", result.value, ". Can use this PRF result concatenated with the hashed challenge to derive the final symetric encryption key");
        console.log("Credential id might need to be stored: ", result.value.id);
        //const myClientExtResults = result.value.getClientExtensionResults();
        //console.log("Credential authenticationExtensionClientOutputs :", myClientExtResults);
        return "Credential created: " + result.value + " . Can use this PRF result concatenated with the hashed challenge to derive the final symetric encryption key";
    }
}

// Derive a symmetric key from the PRF result
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

// Encrypt data with a symmetric key
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

// Decrypt data with a symmetric key
async function decryptData(encryptedData: ArrayBuffer, key: CryptoKey): Promise<string> {
    const encryptedBytes = new Uint8Array(encryptedData);
    const iv = encryptedBytes.slice(0, 12); // Extract the first 12 bytes as IV
    const ciphertext = encryptedBytes.slice(12); // Remaining bytes are the ciphertext
    const decryptedData = await crypto.subtle.decrypt(
        { name: "AES-GCM", iv },
        key,
        ciphertext
    );
    return new TextDecoder().decode(decryptedData);
}
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

function verifyClientExtensionResults(clientExtensionResults: any): void { // todo P0 what type?
    // TODO P1 should return a Result<X>
    if (!clientExtensionResults["hmac-secret"]) {
        throw new Error("hmac-secret is not supported by this authenticator.");
    }
    if (!clientExtensionResults["prf"]) {
        throw new Error("prf is not supported by this authenticator.");
    }
}


// TODO P1, return an interface versus void. Change thrown errors to returns
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
            timeout: makeCredentialTimeout,
            authenticatorSelection: authenticatorSelection,
            extensions: extensions,
            "attestation": attestation,
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

function hexToArrayBuffer(hex: string): ArrayBuffer {
    const bytes = new Uint8Array(hex.match(/.{1,2}/g)!.map(byte => parseInt(byte, 16)));
    return bytes.buffer;
}

async function getExcludeCredentialsFromCreate() {
    // TODO P1 Prevent re-registration of the same authenticator to ensure uniqueness:
    // Prevent re-registration of the same authenticator to ensure uniqueness:
    return [{
        // type: "public-key",
        // id: "<existing-credential-id>"
        // transports: ["usb", "nfc", "ble", "internal"]
    }];
}
export async function createCred() {

    /* 
    // possible values: none, direct, indirect
    let attestation_type = "none";
    // possible values: <empty>, platform, cross-platform
    let authenticator_attachment = "";
    // possible values: preferred, required, discouraged
    let user_verification = "required";
    // possible values: discouraged, preferred, required
    let residentKey = "discouraged";

    // prepare form post data
    var data = new FormData();
    // data.append('username', username);
    // data.append('displayName', displayName);
    data.append('attType', attestation_type);
    data.append('authType', authenticator_attachment);
    data.append('userVerification', user_verification);
    data.append('residentKey', residentKey);
    */

    // send to server for registering

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
        "rp": rpForCreate,
        "user": user,
        "challenge": challenge, // hexToArrayBuffer("7b226e616d65223a2250616e6b616a222c22616765223a32307d"), // Converted to ArrayBuffer. // TODO P1 example to real?
        // or new Uint8Array(32);
        "pubKeyCredParams": pubKeyCredParams,
        "timeout": makeCredentialTimeout,
        "attestation": attestation,
        "attestationFormats": [],
        authenticatorSelection: authenticatorSelection,
        "hints": [],
        // "excludeCredentials": excludeCredentials,
        "extensions": extensions,
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
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////


/**
 * This value is for sake of demonstration. Pick 32 random
 * bytes. `salt` can be static for your site or unique per
 * credential depending on your needs.
 */
const firstSalt: Uint8Array = crypto.getRandomValues(new Uint8Array(12)); // TODO P1 Salt...

export async function registerCredential(): Promise<void> {

    // Define the extensions with the PRF extension
    const extensions22: AuthenticationExtensionsClientInputs & {
        prf: { eval?: any /* ArrayBuffer*/, evalContext?: ArrayBuffer }
    } = {
        prf: {                                    
            eval: {
                first: firstSalt,
            },  // PRF evaluation inputs
            evalContext: new Uint8Array([0x04, 0x05, 0x06]).buffer, // Optional evaluation context
        }
    };

    // Credential Creation Request
    
    const publicKey: any = { // PublicKeyCredentialCreationOptionsWithPRF = { // /*/ PublicKeyCredentialCreationOptions = {
        // Basic registration parameters
        rp: { name: "Example RP", /* id: 'chrome-extension://pniimiboklghaffelpegjpcgobgamkal' */ }, // TODO P0  rpForCreate, // { name: "Example RP" },
        user: {
            id: new Uint8Array([5, 6, 7, 8]), // TODO P0 Replace with the user's unique ID
            name: "user@example.com",
            displayName: "User Example",
        },
        challenge: crypto.getRandomValues(new Uint8Array(32)), // new Uint8Array(32), // TODO P0 Replace with a secure, random value
        pubKeyCredParams: pubKeyCredParams, // [{ type: "public-key", alg: -7 }], // ECDSA w/ SHA-256
        authenticatorSelection: authenticatorSelection,
        extensions: extensions22,
        attestation: "none"
    };
    // console.log("credentialCreationOptions: ", publicKey);

    let credential: PublicKeyCredential;
    try {
        credential = await navigator.credentials.create({
            publicKey
        }) as PublicKeyCredential; // TODO P0 or PUblicKeyCredential or any?
        console.log("credential: ", credential);

        // return;
        // confirm shape of expected result

        //const authenticationExtensionsClientOutputs = credential.getClientExtensionResults();
        //console.log("authenticationExtensionsClientOutputs:", authenticationExtensionsClientOutputs);
        //if (!(authenticationExtensionsClientOutputs as any)?.prf?.enabled) {
        //    console.log("authenticationExtensionsClientOutputs or prf or enable not available ????")
        //    // return;
        //}
        // return;

        // console.log("clientExtensions2: first:", authenticationExtensionsClientOutputs.prf?.results?.first);

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

        const auth1ExtensionResults = assertion.getClientExtensionResults();
        console.log("auth1ExtensionResults: ", auth1ExtensionResults);
        //   prf: {
        //     results: {
        //       first: ArrayBuffer(32),
        //     }
        //   }
        // }

        if (!((auth1ExtensionResults as any).prf?.results?.first)) {
            console.log("This authenticator is not supported. Did not return PRF results.");
            return;
        }
        console.log("Good, this authenticator supports PRF.");

        //const authData = new Uint8Array((assertion.response as AuthenticatorAssertionResponse).authenticatorData);
        //const clientDataJSON = new Uint8Array((assertion.response as AuthenticatorAssertionResponse).clientDataJSON);
        //const signature = new Uint8Array((assertion.response as AuthenticatorAssertionResponse).signature);

        //// Perform signature validation
        //validateSignature(authData, clientDataJSON, signature, credentialIdBase64);
    });
};

const validateSignature = async (
    authData: Uint8Array,
    clientDataJSON: Uint8Array,
    signature: Uint8Array,
    credentialIdBase64: string
): Promise<void> => {
    // Retrieve the public key from storage or other source
    chrome.storage.sync.get("publicKey", async (result) => {
        const publicKeyPEM = result.publicKey;
        if (!publicKeyPEM) {
            console.error("No public key found for validation.");
            return;
        }

        // Convert PEM to CryptoKey
        const publicKey = await importPublicKey(publicKeyPEM);

        // Construct the data to validate: concatenated authData + clientDataHash
        const clientDataHash = await crypto.subtle.digest("SHA-256", clientDataJSON);
        const dataToValidate = new Uint8Array([...authData, ...new Uint8Array(clientDataHash)]);

        // Verify the signature
        const isValid = await crypto.subtle.verify(
            {
                name: "ECDSA",
                hash: { name: "SHA-256" },
            },
            publicKey,
            signature,
            dataToValidate
        );

        if (isValid) {
            console.log("Signature is valid!");
        } else {
            console.error("Invalid signature.");
        }
    });
};

const importPublicKey = async (pem: string): Promise<CryptoKey> => {
    // Convert PEM string to ArrayBuffer
    const pemHeader = "-----BEGIN PUBLIC KEY-----";
    const pemFooter = "-----END PUBLIC KEY-----";
    const pemContents = pem
        .replace(pemHeader, "")
        .replace(pemFooter, "")
        .replace(/\s/g, "");
    const binaryDerString = atob(pemContents);
    const binaryDer = new Uint8Array(binaryDerString.length);
    for (let i = 0; i < binaryDerString.length; i++) {
        binaryDer[i] = binaryDerString.charCodeAt(i);
    }

    // Import the key
    return crypto.subtle.importKey(
        "spki",
        binaryDer.buffer,
        {
            name: "ECDSA",
            namedCurve: "P-256",
        },
        true,
        ["verify"]
    );
};







// Derive Seed Material Using PRF During Authentication
// Use the PRF extension to derive deterministic key material during authentication.

export const deriveSeedMaterial = async (): Promise<void> => {
    chrome.storage.sync.get("credentialId", async (result) => {
        const credentialIdBase64 = result.credentialId;
        if (!credentialIdBase64) {
            console.error("No credential ID found.");
            return;
        }
        const options: any /* PublicKeyCredentialRequestOptions */ = {
            challenge: crypto.getRandomValues(new Uint8Array(32)),
            allowCredentials: [{
                id: Uint8Array.from(atob(credentialIdBase64), c => c.charCodeAt(0)),
                type: "public-key",
            }],
            authenticatorSelection: authenticatorSelection,
            extensions: {
                prf: {
                    inputs: [{ type: "hkdf", salt: crypto.getRandomValues(new Uint8Array(16)) }]
                }
            },
        };

        const assertion = await navigator.credentials.get({
            publicKey: options,
        }) as PublicKeyCredential;
        console.log("assertion: ", assertion);
        console.log("assertion extensionResults :", assertion.getClientExtensionResults());
        console.log("PRF Output:", (assertion.getClientExtensionResults() as any).prf.outputs);
    });
};


   

