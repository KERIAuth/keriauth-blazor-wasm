// This must be kept in sync with the model RegisteredAuthenticators.cs
export interface IRegisteredAuthenticator {
    name?: string;

    // The credential in Base64 format.
    credential: string;

    // The encrypted passcode in Base64 format.
    encryptedPasscodeBase64?: string;

    // The UTC time when the authenticator was registered.
    registeredUtc: string; // DateTime maps to ISO 8601 string in JSON.

    //  The UTC time when this record was last updated.
    lastUpdatedUtc: string; // DateTime maps to ISO 8601 string in JSON.
}

