namespace Extension.Services.Storage;

using Extension.Models.Storage;
using FluentResults;
using Microsoft.Extensions.Logging;

/// <summary>
/// Generic observer for storage changes in a specific storage area.
/// Simplifies subscription management for storage models.
///
/// Usage:
/// <code>
/// var observer = new StorageObserver&lt;Preferences&gt;(
///     storageService,
///     StorageArea.Local,
///     prefs => Console.WriteLine($"Preferences changed: {prefs.IsDarkTheme}")
/// );
/// // Dispose when done: observer.Dispose();
/// </code>
/// </summary>
/// <typeparam name="T">Storage model type to observe (must implement IStorageModel)</typeparam>
public class StorageObserver<T> : IObserver<T>, IDisposable where T : class, IStorageModel {
    private readonly IStorageService _storageService;
    private readonly StorageArea _storageArea;
    private readonly Action<T> _onNext;
    private readonly Action<Exception>? _onError;
    private readonly Action? _onCompleted;
    private readonly ILogger? _logger;
    private IDisposable? _subscription;
    private bool _disposed;

    /// <summary>
    /// Creates a storage observer and automatically subscribes.
    /// </summary>
    /// <param name="storageService">Storage service instance</param>
    /// <param name="storageArea">Storage area to monitor</param>
    /// <param name="onNext">Callback when value changes (optional, defaults to no-op)</param>
    /// <param name="onError">Optional error handler</param>
    /// <param name="onCompleted">Optional completion handler</param>
    /// <param name="logger">Optional _logger for diagnostics</param>
    public StorageObserver(
        IStorageService storageService,
        StorageArea storageArea,
        Action<T> onNext,
        Action<Exception>? onError = null,
        Action? onCompleted = null,
        ILogger? logger = null
    ) {
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _storageArea = storageArea;
        _onNext = onNext;
        _onError = onError;
        _onCompleted = onCompleted;
        _logger = logger;

        // Auto-subscribe on construction
        _subscription = _storageService.Subscribe<T>(this, _storageArea);
        _logger?.LogDebug("StorageObserver<{Type}> subscribed to {Area} storage", typeof(T).Name, _storageArea);
    }

    public void OnNext(T value) {
        try {
            _onNext(value);
        }
        catch (Exception ex) {
            _logger?.LogError(ex, "Error in StorageObserver<{Type}> OnNext handler", typeof(T).Name);
            OnError(ex);
        }
    }

    public void OnError(Exception error) {
        _logger?.LogError(error, "StorageObserver<{Type}> received error", typeof(T).Name);
        _onError?.Invoke(error);
    }

    public void OnCompleted() {
        _logger?.LogDebug("StorageObserver<{Type}> completed", typeof(T).Name);
        _onCompleted?.Invoke();
    }

    /// <summary>
    /// Gets the current value of the observed model from storage.
    /// </summary>
    /// <returns>Result containing the current value, or null if not found</returns>
    public async Task<Result<T?>> Get() {
        // TODO P2 this is now an anti-pattern, as consumers should directly use IStorageService, or rely on OnNext(). Consider deleting.
        return await _storageService.GetItem<T>(_storageArea);
    }

    public void Dispose() {
        if (_disposed) return;

        _subscription?.Dispose();
        _subscription = null;
        _disposed = true;
        _logger?.LogDebug("StorageObserver<{Type}> disposed", typeof(T).Name);
        GC.SuppressFinalize(this);
    }
}
