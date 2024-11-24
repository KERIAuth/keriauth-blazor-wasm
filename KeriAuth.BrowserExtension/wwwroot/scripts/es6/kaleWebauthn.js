// based on https://gist.github.com/MasterKale/dbe39a01438251f0cbd55576304731fd
// TODO P2 add acknowledgement


// export const KaleWebauthn = {

/**
 * Performas user registration and generates a registration credential.
 * @ returns {Object} A registration credential object.
 */

let regCredential;
const firstSalt = new Uint8Array(new Array(32).fill(1)).buffer;

export async function register() {
    console.log(`registering...`);
}
export async function register2() {
    regCredential = await navigator.credentials.create({
        publicKey: {
            challenge: new Uint8Array([1, 2, 3, 4]), // Example value
            rp: {
                name: "localhost PRF demo",
                // id: "localhost",
            },
            user: {
                id: new Uint8Array([5, 6, 7, 8]), // Example value
                name: "user@localhost",
                displayName: "user@localhost",
            },
            pubKeyCredParams: [
                { alg: -8, type: "public-key" }, // Ed25519
                { alg: -7, type: "public-key" }, // ES256
                { alg: -257, type: "public-key" }, // RS256
            ],
            authenticatorSelection: {
                userVerification: "required",
            },
            extensions: {
                prf: {
                    eval: {
                        first: firstSalt,
                    },
                },
            },
        },
    });

    console.log(`registering 3...`);

    const extensionResults = regCredential.getClientExtensionResults();
    console.log("extensionResults:", extensionResults);
    // Looking for something like this
    // {
    //   prf: {
    //     enabled: true
    //   }
    // }
    const prfSupported = !!(
        extensionResults.prf && extensionResults.prf.enabled
    );
    console.log(`PRF supported: ${prfSupported}`);
    // return extensionResults;
}

/**
 * Gets PRF results to be used for encryption/decryption based on previously registered credential.
 * @ param {Object} regCredential - The registration credential generated during registration.
 * @ returns TBD
 */
export async function authenticate(regCredential) {


    if (!regCredential || !regCredential.id || !regCredential.publicKey) {
        console.error("Invalid registration credential provided.");
        return false;
    }



    const auth1Credential = await navigator.credentials.get({
        publicKey: {
            challenge: new Uint8Array([9, 0, 1, 2]), // Example value
            allowCredentials: [
                {
                    id: regCredential.rawId,
                    transports: regCredential.response.getTransports(),
                    type: "public-key",
                },
            ],
            rpId: "localhost",
            // This must always be either "discouraged" or "required".
            // Pick one and stick with it.
            userVerification: "required",
            extensions: {
                prf: {
                    eval: {
                        first: firstSalt,
                    },
                },
            },
        },
    });

    const auth1ExtensionResults =
        auth1Credential.getClientExtensionResults();
    console.log('Auth extension results:', auth1ExtensionResults);

    const inputKeyMaterial = new Uint8Array(
        auth1ExtensionResults.prf.results.first
    );
    const keyDerivationKey = await crypto.subtle.importKey(
        "raw",
        inputKeyMaterial,
        "HKDF",
        false,
        ["deriveKey"]
    );

    // Never forget what you set this value to or the key can't be
    // derived later
    const label = "encryption key";
    const info = new TextEncoder().encode(label);
    // `salt` is a required argument for `deriveKey()`, but should
    // be empty
    const salt = new Uint8Array();

    const encryptionKey = await crypto.subtle.deriveKey(
        { name: "HKDF", info, salt, hash: "SHA-256" },
        keyDerivationKey,
        { name: "AES-GCM", length: 256 },
        // No need for exportability because we can deterministically
        // recreate this key
        false,
        ["encrypt", "decrypt"]
    );

    // Keep track of this `nonce`, you'll need it to decrypt later!
    // FYI it's not a secret so you don't have to protect it.
    const nonce = crypto.getRandomValues(new Uint8Array(12));

    const encrypted = await crypto.subtle.encrypt(
        { name: "AES-GCM", iv: nonce },
        encryptionKey,
        new TextEncoder().encode("hello readers 🥳")
    );

    const decrypted = await crypto.subtle.decrypt(
        // `nonce` should be the same value from Step 2.3
        { name: "AES-GCM", iv: nonce },
        encryptionKey,
        encrypted
    );

    const decodedMessage = new TextDecoder().decode(decrypted);
    console.log(`Decoded message: "${decodedMessage}"`);
    // hello readers 🥳

}
// };

