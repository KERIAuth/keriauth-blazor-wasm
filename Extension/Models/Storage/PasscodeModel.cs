namespace Extension.Models.Storage;

using System.Text.Json.Serialization;

/// <summary>
/// Passcode and session expiration stored atomically in session storage.
/// ATOMIC DESIGN: Passcode and SessionExpirationUtc are stored together to ensure
/// reactive listeners (AppCache, SessionManager observers) never see intermediate state
/// where passcode exists but expiration is missing.
///
/// Storage key: "PasscodeModel"
/// Storage area: Session
/// Lifetime: Cleared on browser close, persists across service worker restarts
/// </summary>
public record PasscodeModel : IStorageModel {
    /// <summary>
    /// The user's passcode in plaintext.
    /// WARNING: Stored unencrypted in session storage. Should be cleared after timeout.
    /// </summary>
    [JsonPropertyName("Passcode")]
    public required string Passcode { get; init; }

    /// <summary>
    /// UTC timestamp when the current session should expire due to inactivity.
    /// Session expires when current time exceeds this value.
    /// ATOMIC: Stored with passcode to ensure consistent state in storage observers.
    /// </summary>
    [JsonPropertyName("SessionExpirationUtc")]
    public required DateTime SessionExpirationUtc { get; init; } = DateTime.MinValue; // expired default
}
