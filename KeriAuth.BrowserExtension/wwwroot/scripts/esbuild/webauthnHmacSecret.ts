// webauthnHmacSecret.ts

// Background Script: browser extension core logic

// Utility to generate a random challenge

import { decode as decodeCBOR, encode as encodeCBOR } from '@cbor'; // 'cbor';
function generateRandomChallenge(): Uint8Array {
    const challenge = new Uint8Array(32);
    window.crypto.getRandomValues(challenge);
    return challenge;
}

// Utility to store credentials in chrome.storage.sync
export async function storeCredential(credentialId: ArrayBuffer, publicKey: JsonWebKey): Promise<void> {
    console.log("storeCredential 1");
    const key = await crypto.subtle.importKey(
        'jwk',
        publicKey,
        { name: 'ECDSA', namedCurve: 'P-256' },
        true,
        []
    );

    console.log("storeCredential 2");
    // Retrieve existing stored credentials
    const stored = (await chrome.storage.sync.get('credentials')) || {};
    const credentials = stored.credentials || [];
    credentials.push({
        credentialId: Array.from(new Uint8Array(credentialId)),
        publicKey: await crypto.subtle.exportKey('jwk', key),
    });

    await chrome.storage.sync.set({ credentials });
}

// Utility to get stored credentials
export async function getStoredCredentials(): Promise<any[]> {
    const stored = (await chrome.storage.sync.get('credentials')) || {};
    return stored.credentials || [];
}

// Register a new credential
export async function registerCredential(): Promise<void> {
    const challenge = generateRandomChallenge();
    const publicKeyCredentialCreationOptions: any = { // PublicKeyCredentialCreationOptions = {
        challenge: challenge,
        rp: { name: 'Example Extension' },
        user: {
            id: new Uint8Array(16), // Static user ID (replace with actual logic)
            name: 'user@example.com',
            displayName: 'Example User',
        },
        pubKeyCredParams: [
            { alg: -7, type: 'public-key' }, // ECDSA w/ SHA-256
            { alg: -257, type: "public-key" }, // RS256
        ],
        authenticatorSelection: { /* authenticatorAttachment: 'platform', */ requireResidentKey: true },
        timeout: 60000,
        extensions: {
            'hmac-secret': true, // Request HMAC secret
        },
        attestation: "direct",
    };
    try {
        const credential = await navigator.credentials.create({
            publicKey: publicKeyCredentialCreationOptions,
        }) as PublicKeyCredential;
        if (credential) {
            console.log('Credential created:', credential);

            // check if authenticator supports hmac-secret
            console.log('Client Extension Results:', credential.getClientExtensionResults());



            const { rawId, response } = credential;
            const attestationObject = (response as AuthenticatorAttestationResponse).attestationObject;

            // Decode the attestationObject (CBOR format)
            const decodedAttestationObject = decodeCBOR(new Uint8Array(attestationObject));
            console.log('Decoded attestation object:', decodedAttestationObject);

            const authData = decodedAttestationObject.authData;
            console.log("registerCredential 5");
            // Convert to ArrayBuffer if necessary
            const authDataArrayBuffer = authData instanceof ArrayBuffer ? authData : authData.buffer;
            console.log("registerCredential 5.5");
            // Extract public key from authData
            const publicKey = extractPublicKeyFromAuthData(authDataArrayBuffer);
            console.log("registerCredential 6");
            // Store the credential ID and public key
            await storeCredential(rawId, publicKey);
            console.log("registerCredential 7");
        }
    } catch (error) {
        console.error('Error creating credential:', error);
        //Common errors include:
        // NotAllowedError: User canceled or action timed out.  // TODO P1 return something
        // ConstraintError: An unsupported extension was requested.
    }
}

function extractPublicKeyFromAuthData(authData: ArrayBuffer): JsonWebKey {
    console.log("extractPublicKeyFromAuthData authData: ", authData);
    const authDataView = new DataView(authData);

    let offset = 0;

    // RP ID Hash: 32 bytes
    const rpIdHash = authData.slice(offset, offset + 32);
    offset += 32;

    // Flags: 1 byte
    const flags = authDataView.getUint8(offset);
    offset += 1;

    // Sign Count: 4 bytes
    const signCount = authDataView.getUint32(offset, false); // Big-endian
    offset += 4;

    console.log('RP ID Hash:', rpIdHash);
    console.log('Flags:', flags);
    console.log('Sign Count:', signCount);

    // AAGUID: 16 bytes
    const aaguid = authData.slice(offset, offset + 16);
    offset += 16;

    console.log('AAGUID:', aaguid);

    // Credential ID Length: 2 bytes
    const credentialIdLength = authDataView.getUint16(offset, false); // Big-endian
    offset += 2;

    console.log('Credential ID Length:', credentialIdLength);

    // Credential ID: Variable length
    const credentialId = authData.slice(offset, offset + credentialIdLength);
    offset += credentialIdLength;

    console.log('Credential ID:', credentialId);

    console.log("remaining offset, byteLength: ", offset, authData.byteLength);

    // Public Key: Remaining bytes (CBOR-encoded)
    const publicKeyBytes = new Uint8Array(authData.slice(offset));

    console.log('Public Key Bytes:', publicKeyBytes);

    // Decode the CBOR public key
    const publicKeyCBOR = decodeCBOR(publicKeyBytes);

    console.log('Public Key (CBOR Decoded):', publicKeyCBOR);
    const jwk = publicKeyCBOR as JsonWebKey;
    console.log('jwk:', jwk);
    return jwk;
}


// Authenticate using stored credentials
async function authenticate(challenge: Uint8Array): Promise<void> {
    const storedCredentials = await getStoredCredentials();

    const publicKeyCredentialRequestOptions: PublicKeyCredentialRequestOptions = {
        challenge,
        allowCredentials: storedCredentials.map(cred => ({
            id: new Uint8Array(cred.credentialId).buffer,
            type: 'public-key',
        })),
        timeout: 60000,
    };

    try {
        const credential = await navigator.credentials.get({
            publicKey: publicKeyCredentialRequestOptions,
        }) as PublicKeyCredential;

        if (credential) {
            console.log('Credential retrieved:', credential);

            const { rawId, response } = credential;
            const authenticatorData = (response as AuthenticatorAssertionResponse).authenticatorData;
            const clientDataJSON = (response as AuthenticatorAssertionResponse).clientDataJSON;
            const signature = (response as AuthenticatorAssertionResponse).signature;

            console.log('Authenticator data:', authenticatorData);
            console.log('Client data JSON:', clientDataJSON);

            // Validate the signature using stored public key
            const publicKey = storedCredentials.find(cred =>
                new Uint8Array(cred.credentialId).every((val, i) => val === new Uint8Array(rawId)[i])
            )?.publicKey;

            if (publicKey) {
                const key = await crypto.subtle.importKey(
                    'jwk',
                    publicKey,
                    { name: 'ECDSA', namedCurve: 'P-256' },
                    true,
                    ['verify']
                );

                const valid = await crypto.subtle.verify(
                    { name: 'ECDSA', hash: { name: 'SHA-256' } },
                    key,
                    signature,
                    authenticatorData
                );

                console.log('Authentication valid:', valid);
            }
        }
    } catch (error) {
        console.error('Error authenticating:', error);
    }
}


//// Example usage
//chrome.runtime.onInstalled.addListener(() => {
//    console.log('Extension installed!');
//    registerCredential().then(() => console.log('Registration complete.'));
//});

//// Listen for authentication requests
//chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
//    if (message.type === 'authenticate') {
//        const challenge = generateRandomChallenge();
//        authenticate(challenge).then(() => sendResponse({ success: true }));
//        return true; // Indicate async response
//    }
//});
