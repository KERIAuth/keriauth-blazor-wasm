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
 * Logs messages in a structured format, with optional debug flag.
 */
function logMessage(level: "INFO" | "WARN" | "ERROR", message: string, debug: boolean = false): void {
    if (debug) {
        console.log(JSON.stringify({ level, message, timestamp: new Date().toISOString() }));
    }
}

/**
 * Validates the User object, ensuring required fields are non-empty.
 * @param user The User object to validate.
 * @returns Result indicating success or validation error.
 */
function validateUser(user: User): Result<void> {
    if (!user.name || user.name.trim() === "") {
        return { ok: false, error: { code: ErrorCode.VALIDATION_ERROR, message: "The 'name' field must be a non-empty string." } };
    }
    if (!user.displayName || user.displayName.trim() === "") {
        return { ok: false, error: { code: ErrorCode.VALIDATION_ERROR, message: "The 'displayName' field must be a non-empty string." } };
    }
    if (user.id.byteLength < 16 || user.id.byteLength > 32) {
        return { ok: false, error: { code: ErrorCode.VALIDATION_ERROR, message: "The 'id' field must be a Uint8Array with a length between 16 and 32 bytes." } };
    }
    return { ok: true, value: undefined };
}

/**
 * Checks for WebAuthn and PRF feature support.
 */
export function checkWebAuthnSupport(): void { // TODO Result<void> {
    //self.PublicKeyCredential;
    //return;
    if (self) {
        console.log("self-full");
    }
    if (window) {
        console.log("window-full");
    }
    if (self.PublicKeyCredential) {
        console.log("yes PublicKeyCredential");
        return; // { ok: false, error: { code: ErrorCode.UNSUPPORTED_FEATURE, message: "WebAuthn is not supported in this browser." } };
    }
    console.log("no PublicKeyCredential");
    return; // { ok: true, value: undefined };
    
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
 * @param rp The relying party identifier.
 * @param user The user details.
 * @param retries Number of retry attempts for the operation.
 * @param debug Enable detailed debugging information.
 */
async function createCredentialWithPRF(
    challenge: Uint8Array,
    rp: string,
    user: User,
    retries: number = 3,
    debug: boolean = false
): Promise<Result<PublicKeyCredential>> {

    // TODO P0 uncomment
    /*
    const supportCheck = checkWebAuthnSupport();
    if (isError(supportCheck)) {
        return { ok: false, error: supportCheck.error };
    }
    */

    const userValidation = validateUser(user);
    if (isError(userValidation)) {
        return { ok: false, error: userValidation.error };
    }

    const publicKey: PublicKeyCredentialCreationOptionsWithPRF = {
        challenge: challenge,
        rp: { name: rp },
        user: {
            id: user.id,
            name: user.name,
            displayName: user.displayName,
        },
        // -7 is in IANA COSE Algorithms registry, to represent ECDSA with SHA-256 on the P-256 curve.
        pubKeyCredParams: [{ alg: -7, type: "public-key" }],
        authenticatorSelection: { userVerification: "required" },
        extensions: { "hmac-secret": true },
    };

    try {
        const credential = await retry(async () => {
            logMessage("INFO", "Attempting to create credential...", debug);
            return await navigator.credentials.create({ publicKey }) as PublicKeyCredential;
        }, retries, 5000); // 5000ms timeout per attempt

        const clientExtensionResults = (credential as any).getClientExtensionResults();
        if (clientExtensionResults?.hmacCreateSecret) {
            logMessage("INFO", "PRF is supported, CTAP2.1 or higher.", debug);
            return { ok: true, value: credential };
        } else {
            logMessage("WARN", "PRF not supported. Likely missing CTAP2.1.", debug);
            return {
                ok: false,
                error: {
                    code: ErrorCode.UNSUPPORTED_FEATURE,
                    message: "CTAP2.1 or higher is required for PRF support."
                }
            };
        }

    } catch (error: unknown) {
        // Type checking for error to determine if it has a message property
        const errorMessage = error instanceof Error ? error.message : String(error);
        const errorCode = errorMessage === ErrorCode.TIMEOUT_ERROR ? ErrorCode.TIMEOUT_ERROR : ErrorCode.UNKNOWN_ERROR;
        return {
            ok: false,
            error: {
                code: errorCode,
                message: `Error during credential creation: ${errorMessage}`
            }
        };
    }

}


// Because chrome.storage.sync is specific per profile and per-extension this identifier will be unique to each profile.
// This method essentially "fingerprints" a profile.
async function getProfileIdentifier(): Promise<string> {
    return new Promise((resolve) => {
        chrome.storage.sync.get(['KERIAuth_profileIdentifier'], (data) => {
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
    const concatenatedInput = `${extensionId}:${profileIdentifier}:${weakPassword}`;
    const hash = crypto.subtle.digest("SHA-256", new TextEncoder().encode(concatenatedInput));
    return new Uint8Array(await hash);
}

// Example usage
async function test() {

    // example ids:
    // generate a Uint8Array from input of the KERIA connection AID and browser profile finterprint.
    const id2 = new TextEncoder().encode("KERIA-connection-AID.prefix" + await getProfileIdentifier());

    const user: User = {
        id: id2,
        name: "user@example.com",
        displayName: "User Example",
    };

    const challenge = await generateChallenge("extensionId...", "weakPassword");

    createCredentialWithPRF(challenge, "rp-extension-id-v1", user, 3, true).then(result => {
        if (isError(result)) {
            console.error("Credential creation failed:", result.error.message);
        } else {
            console.log("Credential created:", result.value, ". Can use this PRF result concatenated with the hashed challenge to derive the final symetric encryption key");

        }
    });
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

const extensionName = "KERI Auth";

async function registerAndEncryptSecret(extensionId: string, weakPassword: string, secret: string): Promise<void> {
    try {
        // Generate a challenge based on inputs to uniquely identify this request
        const challenge = await generateChallenge(extensionId, weakPassword);

        // Define WebAuthn public key options
        const publicKey: PublicKeyCredentialCreationOptions = {
            challenge,
            rp: { name: extensionName, id: extensionId },
            user: {
                id: crypto.getRandomValues(new Uint8Array(32)),
                name: "KERI Auth",
                displayName: "KERI Auth displayName",
            },
            pubKeyCredParams: [{ alg: -7, type: "public-key" }],
            authenticatorSelection: { userVerification: "required" },
            extensions: { hmacCreateSecret: true }
        };

        // Call navigator.credentials.create to create the WebAuthn credential
        const credential = await navigator.credentials.create({ publicKey }) as PublicKeyCredential;

        if (!credential) {
            throw new Error("Credential creation failed: No credential returned.");
        }

        // Verify that PRF is supported
        const clientExtensionResults = (credential as any).getClientExtensionResults();
        if (!clientExtensionResults["hmac-secret"]) {
            throw new Error("PRF not supported by this authenticator.");
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

