/// <reference types="chrome" />

interface User extends PublicKeyCredentialUserEntity {
    id: Uint8Array;
    name: string;
    displayName: string; // TODO: required?
}

type PublicKeyCredentialCreationOptionsWithPRF = PublicKeyCredentialCreationOptions & {
    extensions?: {
        "hmac-secret"?: boolean;
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

// const's used widely in these functions...
const KeriAuthExtensionId = "pniimiboklghaffelpegjpcgobgamkal"; // TODO P1 get the extensionId from chrome.runtime...
const KeriAuthExtensionName = "KERI Auth";
const displayName = "KERI Auth displayName"; // TODO P2
const KeriAuthRp: PublicKeyCredentialRpEntity = { name: KeriAuthExtensionName }; // Note that id is intentionally left off!  See See https://chromium.googlesource.com/chromium/src/+/main/content/browser/webauth/origins.md
const pubKeyCredParams: PublicKeyCredentialParameters[] = [
    { alg: -7, type: "public-key" },   // ES256
    { alg: -257, type: "public-key" }  // RS256
];
const extensions = { // AuthenticationExtensionsClientInputs & { "hmac-secret"?: boolean, "prf"?: any } = {
    "hmac-secret": true,
    "credProps": true, //TODO P1 needed?
    "prf": {   // See https://w3c.github.io/webauthn/#dom-authenticationextensionsprfinputs-eval within https://w3c.github.io/webauthn/#prf-extension
        "eval": {
            "first": new Uint8Array(16)
        }
    },
    // hmacCreateSecret: true,
};

const makeCredentialTimeout = 60000;
const authenticatorSelection = {
    "residentKey": "required",
    "requireResidentKey": true,
    // "userVerification": "preferred",
    // "authenticatorAttachment": "cross-platform"
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

    const user2: PublicKeyCredentialUserEntity = {
        id: user.id,
        name: user.name,
        displayName: user.displayName,
    }
    var challenge = await generateChallenge(KeriAuthExtensionName);
    const credentialCreationOptions: PublicKeyCredentialCreationOptionsWithPRF = {
        challenge: challenge,
        rp: KeriAuthRp,
        user: user2,
        pubKeyCredParams: pubKeyCredParams,
        authenticatorSelection: { userVerification: "required" }, // TODO P1: can use const authenticatorSelection
        extensions: extensions,
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


// Because chrome.storage.sync is specific per profile and per-extension this identifier will be unique to each profile.
// This method essentially "fingerprints" a profile.
export async function getProfileIdentifier(): Promise<string> {
    return new Promise((resolve) => {
        chrome.storage.sync.get(['profileIdentifier'], (data) => {
            if (data.profileIdentifier) {
                resolve(data.profileIdentifier);
            } else {
                // Generate a new identifier (e.g., UUID) for this profile
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
    user = {
        id: id,
        name: KeriAuthExtensionName,
        displayName: displayName,
    };
    return user;
}

function verifyClientExtensionResults(clientExtensionResults: any) : void { // todo P0 what type?
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
        const challenge = await generateChallenge(KeriAuthExtensionName);
        var user = await getOrCreateUser();

        // TODO P0 can these options be in one place for the create options?
        // Define WebAuthn public key options
        const publicKey: any = {  // note while this should be of type PublicKeyCredentialCreationOptions, that interface definition does not yet handles the "prf: true" section.
            challenge: challenge,
            rp: KeriAuthRp,
            user: user,
            pubKeyCredParams: pubKeyCredParams,
            timeout: 60000, // 60 seconds  TODO P2
            authenticatorSelection: authenticatorSelection, // { userVerification: "required" }, // TODO P1: can use const authenticatorSelection
            extensions: extensions,
        };

        const credential = await navigator.credentials.create({ publicKey }) as PublicKeyCredential;
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


///////////////////////////////

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
export async function createCred() {

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
    var user = await getOrCreateUser();
    const publicKey: any = {
        "rp": KeriAuthRp,
        "user": user,
        "challenge": hexToArrayBuffer("7b226e616d65223a2250616e6b616a222c22616765223a32307d"), // Converted to ArrayBuffer. // TODO P1 example to real?
        // or new Uint8Array(32);
        "pubKeyCredParams": pubKeyCredParams,
        "timeout": makeCredentialTimeout,
        "attestation": "none",
        "attestationFormats": [],
        "authenticatorSelection": authenticatorSelection,
        "hints": [],
        "excludeCredentials": [],
        "extensions": extensions,
        
    }
    console.log("Credential Options Formatted", publicKey);

    console.log("Creating PublicKeyCredential...");

    let newCredential; // : Credential | null;
    try {
        newCredential = await navigator.credentials.create({ publicKey  // TODO P1 fix this usage in other .create()'s
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

