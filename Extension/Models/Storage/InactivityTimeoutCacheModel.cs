namespace Extension.Models.Storage;

/// <summary>
/// Session expiration time cached in session storage.
/// Stores when the current session should expire due to inactivity.
/// Replaces legacy string key "inactivityTimeoutMinutes".
///
/// Storage key: "InactivityTimeoutCacheModel"
/// Storage area: Session
/// Lifetime: Cleared on browser close
/// </summary>
public record InactivityTimeoutCacheModel : IStorageModel {
    /// <summary>
    /// UTC timestamp when the current session should expire.
    /// Session expires when current time exceeds this value.
    /// </summary>
    public required DateTime SessionExpirationUtc { get; init; }
}
