using Microsoft.JSInterop;

namespace Extension.Services;

/// <summary>
/// Service for detecting user activity (keydown, mouseup) in the UI context.
/// Sends USER_ACTIVITY messages to BackgroundWorker to extend session expiration.
/// Uses debouncing in TypeScript and throttling in C# to minimize message traffic.
/// </summary>
public interface IUserActivityService : IAsyncDisposable {
    /// <summary>
    /// Starts listening for user activity events on the document.
    /// Should be called when session is unlocked.
    /// </summary>
    Task StartListeningAsync();

    /// <summary>
    /// Stops listening for user activity events.
    /// Should be called when session is locked or component is disposed.
    /// </summary>
    void StopListening();

    /// <summary>
    /// Returns whether the listener is currently active.
    /// </summary>
    bool IsListening { get; }

    /// <summary>
    /// JSInvokable callback invoked from TypeScript when user activity is detected.
    /// This method is already debounced at ~1s in TypeScript.
    /// </summary>
    [JSInvokable]
    Task OnUserActivity();
}
