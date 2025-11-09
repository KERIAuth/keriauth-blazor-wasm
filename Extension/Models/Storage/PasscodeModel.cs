namespace Extension.Models.Storage;

/// <summary>
/// Passcode stored in session storage (cleared when browser closes).
/// Replaces legacy string key "passcode".
///
/// Storage key: "PasscodeModel"
/// Storage area: Session
/// Lifetime: Cleared on browser close, persists across service worker restarts
/// </summary>
public record PasscodeModel {
    /// <summary>
    /// The user's passcode in plaintext.
    /// WARNING: Stored unencrypted in session storage. Should be cleared after timeout.
    /// </summary>
    public required string Passcode { get; init; }
}
