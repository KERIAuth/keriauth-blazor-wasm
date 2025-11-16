namespace Extension.Models.Storage;

using System.Text.Json.Serialization;

/// <summary>
/// Session expiration time cached in session storage.
/// Stores when the current session should expire due to inactivity.
/// Replaces legacy string key "inactivityTimeoutMinutes".
///
/// Storage key: "InactivityTimeoutCacheModel"
/// Storage area: Session
/// Lifetime: Cleared on browser close
/// </summary>
public record SessionExpiration : IStorageModel {
    /// <summary>
    /// UTC timestamp when the current session should expire.
    /// Session expires when current time exceeds this value.
    /// </summary>
    [JsonPropertyName("SessionExpirationUtc")]
    public required DateTime SessionExpirationUtc { get; init; }
}
