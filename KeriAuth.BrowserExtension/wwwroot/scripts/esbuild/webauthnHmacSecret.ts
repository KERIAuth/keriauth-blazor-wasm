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


            // debugging tmp
            console.log('Auth Data Length:', authData.byteLength);
            console.log('Auth Data (Hex):', Array.from(new Uint8Array(authData)).map(byte => byte.toString(16).padStart(2, '0')).join(' '));



            
            // Convert to ArrayBuffer if necessary
            const authDataArrayBuffer = authData instanceof ArrayBuffer ? authData : authData.buffer;
            
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
    // console.log("extractPublicKeyFromAuthData authData: ", authData);

    console.log('AuthData (Hex):', Array.from(new Uint8Array(authData)).map(byte => byte.toString(16).padStart(2, '0')).join(' '));


    const authDataView = new DataView(authData);
    // console.log("authDataView : ", authDataView);
    let offset = 0;

    // Skip RP ID Hash (32 bytes), Flags (1 byte), Sign Count (4 bytes), AAGUID (16 bytes)
    offset += 32 + 1 + 4 + 16;

    //// RP ID Hash: 32 bytes
    //const rpIdHash = authData.slice(offset, offset + 32);
    //console.log('RP ID Hash:', rpIdHash);
    //offset += 32;

    //// Flags: 1 byte
    //const flags = authDataView.getUint8(offset);
    //console.log('Flags:', flags);
    //offset += 1;

    //// Sign Count: 4 bytes
    //const signCount = authDataView.getUint32(offset, false); // Big-endian
    //console.log('Sign Count:', signCount);
    //offset += 4;

    //// AAGUID: 16 bytes
    //const aaguid = authData.slice(offset, offset + 16);
    //console.log('AAGUID:', aaguid);
    //offset += 16;

    // Example usage:
    
    const offset2 = 53; // Credential ID Length offset
    const credentialIdLength2 = extractCredentialIdLength(authData, offset2);
    console.log('Final Credential ID Length:', credentialIdLength2);



    // Credential ID Length: 2 bytes.  // Note that one code suggestion was to read this as a .getUint16(offset, false) // Big-endian.
    const credentialIdLength = authDataView.getUint8(offset); 
    console.log('Credential ID Length:', credentialIdLength);
    offset += 2;

    // Credential ID: Variable length
    const credentialId = authData.slice(offset, offset + credentialIdLength);
    console.log('Credential ID:', credentialId);
    console.log('Credential ID (Hex):', Array.from(new Uint8Array(credentialId)).map(byte => byte.toString(16).padStart(2, '0')).join(' '));
    offset += credentialIdLength;

    // console.log("remaining offset, authDataView.byteLength: authData.byteLength:", offset, authDataView.byteLength, authData.byteLength);

    // TODO P1 next 3 lines are temp test
    //const publicKeyOffset = offset; // Calculate offset based on Credential ID
    //const publicKeyBytes2 = authData.slice(publicKeyOffset);
    //console.log('Public Key Bytes:', new Uint8Array(publicKeyBytes2));


    // Public Key: Remaining bytes (CBOR-encoded)
    const publicKeyBytes = new Uint8Array(authData.slice(offset));
    console.log('Public Key Bytes:', publicKeyBytes);
    // Decode the CBOR public key
    const publicKeyCBOR = decodeCBOR(publicKeyBytes); // TODO P0 currently throws exception "Extra data in input"

    console.log('Public Key (CBOR Decoded):', publicKeyCBOR);
    const jwk = publicKeyCBOR as JsonWebKey;
    console.log('jwk:', jwk);
    return jwk;
}


function extractCredentialIdLength(authData: ArrayBuffer, offset: number): number {
    const authDataView = new DataView(authData);

    // Log bytes around offset for debugging
    const bytesAroundOffset = new Uint8Array(authData.slice(offset - 2, offset + 4));
    console.log('Bytes Around Offset:', Array.from(bytesAroundOffset).map(b => b.toString(16).padStart(2, '0')).join(' '));

    // Attempt to read the Credential ID Length
    const credentialIdLength = authDataView.getUint16(offset, false); // Big-endian
    console.log('Credential ID Length (getUint16):', credentialIdLength);

    // Manual parsing for validation
    const byte1 = authDataView.getUint8(offset);
    const byte2 = authDataView.getUint8(offset + 1);
    const manualCredentialIdLength = (byte1 << 8) | byte2; // Big-endian

    console.log('Credential ID Length (manual):', manualCredentialIdLength);

    // Assert both methods produce the same result
    console.assert(
        credentialIdLength === manualCredentialIdLength,
        `Mismatch: getUint16=${credentialIdLength}, manual=${manualCredentialIdLength}`
    );

    return credentialIdLength;
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
