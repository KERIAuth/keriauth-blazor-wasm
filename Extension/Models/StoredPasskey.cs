using System.Text.Json.Serialization;

namespace Extension.Models;

/// <summary>
/// Schema version for RegisteredAuthenticator model.
/// Increment when making breaking changes to the data format.
/// </summary>
public static class RegisteredAuthenticatorSchema {
    /// <summary>
    /// Current schema version.
    /// Version 2: Added Transports, SchemaVersion. Changed key derivation from HKDF to SHA-256.
    /// Version 3: Added PasscodeHash to detect configuration consistency.
    /// Version 4: Added Aaguid, Icon for descriptive names and visual identification.
    /// Old registrations (version 1-3 or missing version) are invalid and must be re-registered.
    /// </summary>
    public const int CurrentVersion = 4;
}

public record RegisteredAuthenticator {
    /// <summary>
    /// Schema version of this registration.
    /// Used for forward compatibility checks.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Base64URL-encoded credential ID from WebAuthn registration.
    /// </summary>
    [JsonPropertyName("credential")]
    public required string CredentialBase64 { get; init; }

    /// <summary>
    /// Transport types supported by this authenticator (e.g., "usb", "nfc", "ble", "internal", "hybrid").
    /// Used to provide transport hints during authentication for better UX.
    /// </summary>
    [JsonPropertyName("transports")]
    public required string[] Transports { get; init; }

    /// <summary>
    /// Passcode encrypted with AES-GCM using key derived from PRF output.
    /// Key derivation: SHA256(profileId || prfOutput || "KERI Auth").
    /// </summary>
    [JsonPropertyName("encryptedPasscodeBase64")]
    public required string EncryptedPasscodeBase64 { get; init; }

    /// <summary>
    /// Hash of the passcode at the time of registration.
    /// Used to detect if the authenticator is consistent with the current configuration.
    /// Copied from KeriaConnectConfig.PasscodeHash during registration.
    /// </summary>
    [JsonPropertyName("passcodeHash")]
    public required int PasscodeHash { get; init; }

    /// <summary>
    /// AAGUID of the authenticator in UUID format (e.g., "08987058-cadc-4b81-b6e1-30de50dcbe96").
    /// Used to look up the authenticator's friendly name and icon from FIDO metadata.
    /// </summary>
    [JsonPropertyName("aaguid")]
    public required string Aaguid { get; init; }

    /// <summary>
    /// Data URL for the authenticator's icon (e.g., "data:image/png;base64,...").
    /// Retrieved from FIDO convenience metadata during registration.
    /// May be null if the authenticator is not in the metadata.
    /// </summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    [JsonPropertyName("registeredUtc")]
    public required DateTime CreationTime { get; init; }

    [JsonPropertyName("lastUpdatedUtc")]
    public DateTime LastUpdatedUtc { get; init; } = DateTime.UtcNow;

    // TODO P2 create a last successfully used property? Perhaps as a separate structure in storage
}
