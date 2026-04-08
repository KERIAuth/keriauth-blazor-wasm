using Extension.Services.JsBindings;
using Microsoft.JSInterop;

namespace Extension.Services;

/// <summary>
/// Monitors browser network connectivity (navigator.onLine) in the service worker context.
/// Uses a pre-loaded TypeScript module (via JsModuleLoader) that listens for online/offline
/// events on `self` and calls back into C# via DotNetObjectReference.
///
/// This service runs in the BackgroundWorker DI container only.
/// BackgroundWorker subscribes to OnlineStateChanged and writes NetworkState to session storage.
/// </summary>
public class NetworkConnectivityService : INetworkConnectivityService {
    private readonly IJsModuleLoader _moduleLoader;
    private readonly ILogger<NetworkConnectivityService> _logger;

    private DotNetObjectReference<NetworkConnectivityService>? _dotNetRef;
    private bool _isListening;
    private bool _isDisposed;

    private IJSObjectReference Module => _moduleLoader.GetModule("networkConnectivityListener");

    public NetworkConnectivityService(
        IJsModuleLoader moduleLoader,
        ILogger<NetworkConnectivityService> logger) {
        _moduleLoader = moduleLoader;
        _logger = logger;
    }

    public bool IsOnline { get; private set; } = true;

    public event Action<bool>? OnlineStateChanged;

    public async Task StartListeningAsync() {
        if (_isDisposed) return;

        try {
            _dotNetRef ??= DotNetObjectReference.Create(this);

            // Always call startListening — idempotent, re-reports current state on re-call
            await Module.InvokeVoidAsync("startListening", _dotNetRef);

            _isListening = true;
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(NetworkConnectivityService) + ": Failed to start listening");
        }
    }

    public void StopListening() {
        if (!_isListening) return;

        try {
            _ = StopListeningInternalAsync();
            _isListening = false;
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, nameof(NetworkConnectivityService) + ": Error stopping listener");
        }
    }

    private async Task StopListeningInternalAsync() {
        try {
            await Module.InvokeVoidAsync("stopListening");
        }
        catch (JSDisconnectedException) {
            // Expected during shutdown
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, nameof(NetworkConnectivityService) + ": Error in " + nameof(StopListeningInternalAsync));
        }
    }

    [JSInvokable]
    public Task OnNetworkStateChanged(bool isOnline) {
        if (_isDisposed) return Task.CompletedTask;

        var changed = IsOnline != isOnline;
        IsOnline = isOnline;

        if (changed) {
            _logger.LogInformation(nameof(NetworkConnectivityService) + ": Network state changed — IsOnline={IsOnline}", isOnline);
            OnlineStateChanged?.Invoke(isOnline);
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() {
        if (_isDisposed) return;
        _isDisposed = true;

        StopListening();

        _dotNetRef?.Dispose();
        _dotNetRef = null;

        GC.SuppressFinalize(this);
    }
}
