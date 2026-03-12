namespace Extension.Models.Storage;

using System.Text.Json.Serialization;

/// <summary>
/// Session state stored in chrome.storage.session.
/// Contains only the session expiration time — no sensitive data.
/// The passcode is kept exclusively in BackgroundWorker process memory (SessionManager._passcode).
///
/// Storage key: "SessionStateModel"
/// Storage area: Session
/// Lifetime: Cleared on browser close; if SW is force-restarted, session locks immediately (passcode is lost from memory).
/// </summary>
public record SessionStateModel : IStorageModel {
    /// <summary>
    /// UTC timestamp when the current session should expire due to inactivity.
    /// Session expires when current time exceeds this value.
    /// DateTime.MinValue means no active session.
    /// </summary>
    [JsonPropertyName("SessionExpirationUtc")]
    public required DateTime SessionExpirationUtc { get; init; } = DateTime.MinValue; // expired default
}
