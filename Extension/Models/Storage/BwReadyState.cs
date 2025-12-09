namespace Extension.Models.Storage;

using System.Text.Json.Serialization;

/// <summary>
/// Tracks BackgroundWorker initialization state in session storage.
/// Used by App to wait for BackgroundWorker to complete critical initialization
/// (InitializeStorageDefaultsAsync, SessionManager startup) before reading storage.
///
/// IMPORTANT: This is NOT related to user session/authentication state.
/// BwReadyState tracks whether BackgroundWorker has completed its startup tasks,
/// ensuring App doesn't read storage before defaults are created or expired
/// sessions are cleared.
///
/// Storage key: "BwReadyState"
/// Storage area: Session
/// Lifetime: Set after BW init, cleared at start of BW init (to prevent stale flags)
///
/// Flow:
/// 1. BackgroundWorker.OnStartupAsync clears this flag
/// 2. BackgroundWorker completes InitializeStorageDefaultsAsync and SessionManager startup
/// 3. BackgroundWorker sets IsInitialized = true
/// 4. App.razor waits for IsInitialized before calling AppCache.EnsureInitializedAsync
/// </summary>
public record BwReadyState : IStorageModel {
    /// <summary>
    /// True when BackgroundWorker has completed all initialization tasks.
    /// App should wait for this to be true before reading storage.
    /// </summary>
    [JsonPropertyName("IsInitialized")]
    public bool IsInitialized { get; init; }

    /// <summary>
    /// UTC timestamp when BackgroundWorker completed initialization.
    /// Useful for debugging timing issues.
    /// </summary>
    [JsonPropertyName("InitializedAtUtc")]
    public DateTime InitializedAtUtc { get; init; }
}
