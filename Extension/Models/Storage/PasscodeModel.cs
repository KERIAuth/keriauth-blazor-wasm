namespace Extension.Models.Storage;

/// <summary>
/// Passcode stored in session storage (cleared when browser closes).
/// Storage key: "PasscodeModel"
/// Storage area: Session
/// Lifetime: Cleared on browser close, persists across service worker restarts
/// </summary>
public record PasscodeModel : IStorageModel {
    /// <summary>
    /// The user's passcode in plaintext.
    /// WARNING: Stored unencrypted in session storage. Should be cleared after timeout.
    /// </summary>
    public required string Passcode { get; init; }
}
