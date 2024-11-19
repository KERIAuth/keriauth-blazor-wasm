/// <reference types="chrome" />

interface User {
    id: Uint8Array;
    name: string;
    displayName: string;
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

/**
 * Validates the User object, ensuring required fields are non-empty.
 * @param user The User object to validate.
 * @returns Result indicating success or validation error.
 */
function validateUser(user: User): Result<boolean> {
    if (!user.name || user.name.trim() === "") {
        return { ok: false, error: { code: ErrorCode.VALIDATION_ERROR, message: "The 'name' field must be a non-empty string." } };
    }
    if (!user.displayName || user.displayName.trim() === "") {
        return { ok: false, error: { code: ErrorCode.VALIDATION_ERROR, message: "The 'displayName' field must be a non-empty string." } };
    }
    if (user.id.byteLength < 16 || user.id.byteLength > 32) {
        return { ok: false, error: { code: ErrorCode.VALIDATION_ERROR, message: "The 'id' field must be a Uint8Array with a length between 16 and 32 bytes." } };
    }
    return { ok: true, value: true };
}

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

const pubKeyCredParams: PublicKeyCredentialParameters[] = [
    { alg: -7, type: "public-key" },   // ES256
    { alg: -257, type: "public-key" }  // RS256
];

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

    const userValidation: Result<boolean> = validateUser(user);
    if (isError(userValidation)) {
        return { ok: false, error: userValidation.error };
    }

    // prepare parameters for credential creation

    const user2: PublicKeyCredentialUserEntity = {
        id: user.id,
        name: user.name,
        displayName: user.displayName,
    }
    const challenge2 = challenge as BufferSource;
    // window.crypto.getRandomValues(challenge2);  // why here?
    const extensions: AuthenticationExtensionsClientInputs & { "hmac-secret"?: boolean, "prf"?: any } = {
        "hmac-secret": true, "prf": { "eval": { "first": new Uint8Array(16) } },

    };

    const credentialCreationOptions: PublicKeyCredentialCreationOptionsWithPRF = {
        challenge: challenge2,
        rp: KeriAuthRp,
        user: user2,
        pubKeyCredParams: pubKeyCredParams,
        authenticatorSelection: { userVerification: "required" },
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

async function generateChallenge(extensionId: string, weakPassword: string): Promise<Uint8Array> {
    const profileIdentifier = await getProfileIdentifier();
    // Hash the concatenated inputs to create a unique challenge
    const concatenatedInput = `${extensionId}:${profileIdentifier}:${weakPassword}`;  // TODO P2 should the KERIA AID be part of this?
    const hash = crypto.subtle.digest("SHA-256", new TextEncoder().encode(concatenatedInput));
    return new Uint8Array(await hash);
}

// Function to create id2 with a 32-byte hash
// Want this not to be only the profileIdentifier hash, but dependant on the KERIA Connection, since the encrypted passcode will be different depending on that.
async function createId2(): Promise<Uint8Array> {
    const prefix = "KERIA-connection-AID.prefix";  // TODO P2 see above Want
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
    const fullString = prefix + profileIdentifier;
    const uint8Array = hashStringToUint8Array(fullString);
    return uint8Array; // Return the 32-byte hash
}

async function hashStringToUint8Array(input: string): Promise<Uint8Array> {
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

    // example ids:
    // generate a Uint8Array from input of the KERIA connection AID and browser profile fingerprint.
    var id2 = await createId2();
    const user: User = {
        id: id2,
        name: KeriAuthExtensionName, // TODO P2
        displayName: "User Example",  // TODO P2
    };
    const challenge = await generateChallenge(KeriAuthExtensionId, "weakPassword");  // TODO P1 provide user's password
    var result = await createCredentialWithPRF(challenge, user);
    if (isError(result)) {
        console.error("Credential creation failed #11:", result);
        return "Credential creation failed #11:" + result.error.message
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

const KeriAuthExtensionId = "pniimiboklghaffelpegjpcgobgamkal";
const KeriAuthExtensionName = "KERI Auth";
const displayName = "KERI Auth displayName"; // TODO P2
const KeriAuthRp: PublicKeyCredentialRpEntity = { name: KeriAuthExtensionName }; // Note that id is intentionally left off!  See See https://chromium.googlesource.com/chromium/src/+/main/content/browser/webauth/origins.md



async function registerAndEncryptSecret(weakPassword: string, secret: string): Promise<void> {
    try {
        // Generate a challenge based on inputs to uniquely identify this request
        const challenge = await generateChallenge(KeriAuthExtensionName, weakPassword);

        // Define WebAuthn public key options
        const publicKey: any = {  // note while this should be of type PublicKeyCredentialCreationOptions, that interface definition does not yet handles the "prf: true" section.
            challenge,
            rp: KeriAuthRp,
            user: {
                id: crypto.getRandomValues(new Uint8Array(32)),
                name: KeriAuthExtensionName,
                displayName: displayName,
            },
            pubKeyCredParams: pubKeyCredParams,
            timeout: 60000, // 60 seconds  TODO P2
            authenticatorSelection: { userVerification: "required" },
            extensions: {
                // hmacCreateSecret: true,
                "hmac-secret": true,
                prf: { "eval": { "first": new Uint8Array(16) } },
            }
        };

        // Call navigator.credentials.create to create the WebAuthn credential
        const credential = await navigator.credentials.create({ publicKey }) as PublicKeyCredential;

        if (!credential) {
            throw new Error("Credential creation failed #12: No credential returned.");
        }

        // Verify that PRF is supported
        const clientExtensionResults = (credential as any).getClientExtensionResults();
        if (!clientExtensionResults["hmac-secret"]) {
            throw new Error("hmac-secret is not supported by this authenticator.");
        }
        if (!clientExtensionResults["prf"]) {
            throw new Error("prf is not supported by this authenticator.");
        }

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
    let user_verification = "preferred";
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

    const makeCredentialOptions2: any = {
        "rp": {
            // "id": "localhost",
            "name": "FIDO2 Test"
        },
        "user": {
            "name": "KERI Auth", // "xx (Usernameless user created at 11/19/2024 2:11:49 AM)",
            "id": new Uint8Array(16).buffer,  // or, perhaps just {}.
            // "displayName": "John Doe"
        },
        "challenge": hexToArrayBuffer("7b226e616d65223a2250616e6b616a222c22616765223a32307d"), // Converted to ArrayBuffer
        // or new Uint8Array(32);
        "pubKeyCredParams": [
            //{
            //    "type": "public-key",
            //    "alg": -8
            //},
            {
                "type": "public-key",
                "alg": -7
            },
            {
                "type": "public-key",
                "alg": -257
            },
            //{
            //    "type": "public-key",
            //    "alg": -37
            //},
            //{
            //    "type": "public-key",
            //    "alg": -35
            //},
            //{
            //    "type": "public-key",
            //    "alg": -258
            //},
            //{
            //    "type": "public-key",
            //    "alg": -38
            //},
            //{
            //    "type": "public-key",
            //    "alg": -36
            //},
            //{
            //    "type": "public-key",
            //    "alg": -259
            //},
            //{
            //    "type": "public-key",
            //    "alg": -39
            //}
        ],
        "timeout": 60000,
        "attestation": "none",
        "attestationFormats": [],
        "authenticatorSelection": {
            "residentKey": "required",
            "requireResidentKey": true,
            // "userVerification": "preferred",
            // "authenticatorAttachment": "cross-platform"
        },
        "hints": [],
        "excludeCredentials": [],
        "extensions": {
            "exts": true,
            "credProps": true,
            "prf": { "eval": { "first": new Uint8Array(16) } },  // See https://w3c.github.io/webauthn/#dom-authenticationextensionsprfinputs-eval within https://w3c.github.io/webauthn/#prf-extension
            "hmac-secret": true
        }
    }
    console.log("Credential Options Formatted", makeCredentialOptions2);

    console.log("Creating PublicKeyCredential...");

    let newCredential; // : Credential | null;
    try {
        newCredential = await navigator.credentials.create({
            publicKey: makeCredentialOptions2
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

