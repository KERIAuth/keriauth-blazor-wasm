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
    /// Old registrations (version 1 or missing version) are invalid and must be re-registered.
    /// </summary>
    public const int CurrentVersion = 2;
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

    [JsonPropertyName("registeredUtc")]
    public required DateTime CreationTime { get; init; }

    [JsonPropertyName("lastUpdatedUtc")]
    public DateTime LastUpdatedUtc { get; init; } = DateTime.UtcNow;

    // TODO P2 create a last successfully used property? Perhaps as a separate structure in storage
}
