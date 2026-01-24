using Extension.Models.Messages.AppBw;
using Extension.Services.Port;
using Microsoft.JSInterop;

namespace Extension.Services;

/// <summary>
/// Detects user activity (keydown, mouseup) and sends USER_ACTIVITY messages to BackgroundWorker
/// to extend session expiration.
///
/// Architecture:
/// - TypeScript (userActivityListener.ts): Listens for DOM events with 1s debounce
/// - C#: Additional 5s throttle before sending to BackgroundWorker
/// - BackgroundWorker: SessionManager.ExtendIfUnlockedAsync() updates PasscodeModel.SessionExpirationUtc
///
/// This service runs in the App context (popup/tab/sidepanel), not BackgroundWorker.
/// </summary>
public class UserActivityService : IUserActivityService {
    private readonly IJSRuntime _jsRuntime;
    private readonly IAppPortService _portService;
    private readonly ILogger<UserActivityService> _logger;

    // State
    private IJSObjectReference? _module;
    private DotNetObjectReference<UserActivityService>? _dotNetRef;
    private DateTime _lastActivitySentUtc = DateTime.MinValue;
    private bool _isListening;
    private bool _isDisposed;

    public UserActivityService(
        IJSRuntime jsRuntime,
        IAppPortService portService,
        ILogger<UserActivityService> logger) {
        _jsRuntime = jsRuntime;
        _portService = portService;
        _logger = logger;
    }

    public bool IsListening => _isListening;

    /// <summary>
    /// Starts listening for user activity events.
    /// Loads the TypeScript module and registers event listeners.
    /// </summary>
    public async Task StartListeningAsync() {
        if (_isDisposed) {
            // _logger.LogWarning("UserActivityService: Cannot start - already disposed");
            return;
        }

        if (_isListening) {
            // _logger.LogDebug("UserActivityService: Already listening, ignoring StartListeningAsync call");
            return;
        }

        try {
            // Load the TypeScript module (cached by browser after first load)
            _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                "./scripts/es6/userActivityListener.js"
            );

            // Create .NET object reference for JSInvokable callback
            _dotNetRef = DotNetObjectReference.Create(this);

            // Start listening with default options (keydown, mouseup with 1s debounce)
            await _module.InvokeVoidAsync("startListening", _dotNetRef);

            _isListening = true;
            // _logger.LogInformation("UserActivityService: Started listening for user activity");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "UserActivityService: Failed to start listening");
            // Don't throw - activity detection is non-critical functionality
        }
    }

    /// <summary>
    /// Stops listening for user activity events.
    /// </summary>
    public void StopListening() {
        if (!_isListening) {
            return;
        }

        try {
            // Fire-and-forget stop call to JS - use helper to properly handle ValueTask
            if (_module is not null) {
                _ = StopListeningInternalAsync();
            }

            _isListening = false;
            // _logger.LogInformation("UserActivityService: Stopped listening for user activity");
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "UserActivityService: Error stopping listener");
        }
    }

    /// <summary>
    /// Internal async helper for fire-and-forget JS interop call.
    /// </summary>
    private async Task StopListeningInternalAsync() {
        try {
            if (_module is not null) {
                await _module.InvokeVoidAsync("stopListening");
            }
        }
        catch (JSDisconnectedException) {
            // Expected during page unload
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "UserActivityService: Error in StopListeningInternalAsync");
        }
    }

    /// <summary>
    /// JSInvokable callback invoked from TypeScript when user activity is detected.
    /// Already debounced at ~1s in TypeScript. This method applies additional 5s throttle
    /// before sending USER_ACTIVITY message to BackgroundWorker.
    /// </summary>
    [JSInvokable]
    public async Task OnUserActivity() {
        if (_isDisposed) return;

        var now = DateTime.UtcNow;

        // Apply C# throttle: don't send more often than every 5 seconds
        if (now - _lastActivitySentUtc < AppConfig.ThrottleInactivityInterval) {
            // _logger.LogDebug("UserActivityService: Activity detected but throttled (last sent {Seconds}s ago)", (now - _lastActivitySentUtc).TotalSeconds);
            return;
        }

        _lastActivitySentUtc = now;
        // _logger.LogDebug("UserActivityService: User activity detected, sending to BackgroundWorker");

        try {
            var message = new AppBwUserActivityMessage();
            await _portService.SendToBackgroundWorkerAsync(message);
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "UserActivityService: Failed to send USER_ACTIVITY message");
            // Don't throw - activity detection is non-critical functionality
        }
    }

    public async ValueTask DisposeAsync() {
        if (_isDisposed) return;
        _isDisposed = true;

        StopListening();

        // Dispose .NET object reference
        _dotNetRef?.Dispose();
        _dotNetRef = null;

        // Dispose JS module reference
        if (_module is not null) {
            try {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException) {
                // Expected during page unload
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "UserActivityService: Error disposing JS module");
            }
            _module = null;
        }

        _logger.LogDebug("UserActivityService: Disposed");
        GC.SuppressFinalize(this);
    }
}
