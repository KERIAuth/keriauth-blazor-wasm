using System.Text.Json.Serialization;

namespace Extension.Models.Storage;

/// <summary>
/// Stores a unique identifier for the browser profile.
/// Used as part of WebAuthn PRF key derivation to prevent cross-profile credential reuse.
/// Stored in chrome.storage.sync (per-profile, synced across devices).
/// </summary>
public record ProfileIdentifierModel {
    /// <summary>
    /// A randomly-generated UUID that uniquely identifies this browser profile.
    /// Combined with PRF output during key derivation to ensure credentials
    /// cannot be used in a different browser profile.
    /// </summary>
    [JsonPropertyName("profileId")]
    public required string ProfileId { get; init; }
}
