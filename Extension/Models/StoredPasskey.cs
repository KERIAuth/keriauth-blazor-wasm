using System.Text.Json.Serialization;

namespace Extension.Models;

/// <summary>
/// Schema version for StoredPasskey model.
/// Increment when making breaking changes to the data format.
/// </summary>
public static class StoredPasskeySchema {
    /// <summary>
    /// Current schema version.
    /// Old registrations (version 1-6 or missing version) are invalid and must be re-created.
    /// </summary>
    public const int CurrentVersion = 7;
}

public record StoredPasskey {
    /// <summary>
    /// Schema version of this passkey.
    /// Used for forward compatibility checks.
    /// </summary>
    [JsonPropertyName("SchemaVersion")]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    /// <summary>
    /// Base64URL-encoded credential ID from WebAuthn registration.
    /// </summary>
    [JsonPropertyName("CredentialBase64")]
    public required string CredentialBase64 { get; init; }

    /// <summary>
    /// Transport types supported by this passkey's authenticator (e.g., "usb", "nfc", "ble", "internal", "hybrid").
    /// Used to provide transport hints during authentication for better UX.
    /// </summary>
    [JsonPropertyName("Transports")]
    public required string[] Transports { get; init; }

    /// <summary>
    /// Passcode encrypted with AES-GCM using key derived from PRF output.
    /// Key derivation: SHA256(keriaConnectionDigest || prfOutput || "KERI Auth").
    /// </summary>
    [JsonPropertyName("EncryptedPasscodeBase64")]
    public required string EncryptedPasscodeBase64 { get; init; }

    /// <summary>
    /// Digest of the KERIA connection configuration at the time of passkey creation.
    /// Computed as SHA256(ClientAidPrefix + AgentAidPrefix + PasscodeHash).
    /// Used to detect if the passkey is consistent with the current KERIA connection.
    /// </summary>
    [JsonPropertyName("KeriaConnectionDigest")]
    public required string KeriaConnectionDigest { get; init; }

    /// <summary>
    /// AAGUID of the authenticator in UUID format (e.g., "08987058-cadc-4b81-b6e1-30de50dcbe96").
    /// Used to look up the authenticator's friendly name and icon from FIDO metadata.
    /// </summary>
    [JsonPropertyName("Aaguid")]
    public required string Aaguid { get; init; }

    /// <summary>
    /// Data URL for the authenticator's icon (e.g., "data:image/png;base64,...").
    /// Retrieved from FIDO convenience metadata during passkey creation.
    /// May be null if the authenticator is not in the metadata.
    /// </summary>
    [JsonPropertyName("Icon")]
    public string? Icon { get; init; }

    [JsonPropertyName("CreationTime")]
    public required DateTime CreationTime { get; init; }

    [JsonPropertyName("LastUpdatedUtc")]
    public DateTime LastUpdatedUtc { get; init; } = DateTime.UtcNow;
}
