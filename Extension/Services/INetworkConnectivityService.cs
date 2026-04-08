using Microsoft.JSInterop;

namespace Extension.Services;

/// <summary>
/// Service for monitoring browser network connectivity (navigator.onLine)
/// in the BackgroundWorker (service worker) context.
///
/// Fires OnlineStateChanged when connectivity changes.
/// BackgroundWorker subscribes and writes NetworkState to session storage.
/// </summary>
public interface INetworkConnectivityService : IAsyncDisposable {
    /// <summary>
    /// Starts listening for online/offline events.
    /// Idempotent — safe to call on each SW wake (re-reports current state).
    /// </summary>
    Task StartListeningAsync();

    /// <summary>
    /// Stops listening for online/offline events.
    /// </summary>
    void StopListening();

    /// <summary>
    /// Last known online state. Defaults to true.
    /// </summary>
    bool IsOnline { get; }

    /// <summary>
    /// Fired when navigator.onLine state changes.
    /// The bool parameter is the new isOnline value.
    /// </summary>
    event Action<bool>? OnlineStateChanged;

    /// <summary>
    /// JSInvokable callback invoked from TypeScript when connectivity changes.
    /// </summary>
    [JSInvokable]
    Task OnNetworkStateChanged(bool isOnline);
}
