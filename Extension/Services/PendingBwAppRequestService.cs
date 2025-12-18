namespace Extension.Services;

using Extension.Models.Storage;
using Extension.Services.Storage;
using FluentResults;
using Microsoft.Extensions.Logging;

/// <summary>
/// Service for managing pending requests from BackgroundWorker to App.
/// Uses chrome.storage.session for persistence across service worker restarts.
/// </summary>
public class PendingBwAppRequestService : IPendingBwAppRequestService, IDisposable {
    private readonly IStorageService _storageService;
    private readonly ILogger<PendingBwAppRequestService> _logger;
    private readonly List<IObserver<PendingBwAppRequests>> _observers = [];
    private IDisposable? _storageSubscription;

    public event EventHandler<PendingBwAppRequests>? RequestsChanged;

    public PendingBwAppRequestService(
        IStorageService storageService,
        ILogger<PendingBwAppRequestService> logger
    ) {
        _storageService = storageService;
        _logger = logger;

        // Subscribe to storage changes to relay to our observers
        _storageSubscription = _storageService.Subscribe<PendingBwAppRequests>(
            new StorageObserver(this),
            StorageArea.Session
        );

        _logger.LogDebug("PendingBwAppRequestService: initialized");
    }

    public async Task<Result> AddRequestAsync(PendingBwAppRequest request) {
        _logger.LogInformation(
            "AddRequestAsync: requestId={RequestId}, type={Type}",
            request.RequestId, request.Type);

        try {
            // Get current requests
            var getResult = await _storageService.GetItem<PendingBwAppRequests>(StorageArea.Session);
            if (getResult.IsFailed) {
                return Result.Fail(getResult.Errors);
            }

            var current = getResult.Value ?? PendingBwAppRequests.Empty;

            // Log warning if there are already pending requests (initial implementation handles one at a time)
            if (current.Count > 0) {
                // TODO P1 either support multiple requests or don't.
                _logger.LogWarning(
                    "AddRequestAsync: Adding request when {Count} already pending. " +
                    "Current implementation may not process all requests correctly.",
                    current.Count);
            }

            // Add new request
            var updated = current.WithRequest(request);

            // Save to storage
            var setResult = await _storageService.SetItem(updated, StorageArea.Session);
            if (setResult.IsFailed) {
                return Result.Fail(setResult.Errors);
            }

            _logger.LogDebug(
                "AddRequestAsync: Request added, total pending={Count}",
                updated.Count);

            return Result.Ok();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "AddRequestAsync: Failed to add request {RequestId}", request.RequestId);
            return Result.Fail($"Failed to add request: {ex.Message}");
        }
    }

    public async Task<Result> RemoveRequestAsync(string requestId) {
        _logger.LogInformation("RemoveRequestAsync: requestId={RequestId}", requestId);

        try {
            var getResult = await _storageService.GetItem<PendingBwAppRequests>(StorageArea.Session);
            if (getResult.IsFailed) {
                return Result.Fail(getResult.Errors);
            }

            var current = getResult.Value ?? PendingBwAppRequests.Empty;
            var updated = current.WithoutRequest(requestId);

            // If no requests left, remove the storage item entirely
            if (updated.IsEmpty) {
                var removeResult = await _storageService.RemoveItem<PendingBwAppRequests>(StorageArea.Session);
                if (removeResult.IsFailed) {
                    return Result.Fail(removeResult.Errors);
                }
            }
            else {
                var setResult = await _storageService.SetItem(updated, StorageArea.Session);
                if (setResult.IsFailed) {
                    return Result.Fail(setResult.Errors);
                }
            }

            _logger.LogDebug(
                "RemoveRequestAsync: Request removed, remaining={Count}",
                updated.Count);

            return Result.Ok();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "RemoveRequestAsync: Failed to remove request {RequestId}", requestId);
            return Result.Fail($"Failed to remove request: {ex.Message}");
        }
    }

    public async Task<Result<PendingBwAppRequests>> GetRequestsAsync() {
        try {
            var result = await _storageService.GetItem<PendingBwAppRequests>(StorageArea.Session);
            if (result.IsFailed) {
                return Result.Fail<PendingBwAppRequests>(result.Errors);
            }

            return Result.Ok(result.Value ?? PendingBwAppRequests.Empty);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "GetRequestsAsync: Failed to get requests");
            return Result.Fail<PendingBwAppRequests>($"Failed to get requests: {ex.Message}");
        }
    }

    public async Task<Result<PendingBwAppRequest?>> GetRequestAsync(string requestId) {
        try {
            var result = await GetRequestsAsync();
            if (result.IsFailed) {
                return Result.Fail<PendingBwAppRequest?>(result.Errors);
            }

            var request = result.Value.Requests.FirstOrDefault(r => r.RequestId == requestId);
            return Result.Ok(request);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "GetRequestAsync: Failed to get request {RequestId}", requestId);
            return Result.Fail<PendingBwAppRequest?>($"Failed to get request: {ex.Message}");
        }
    }

    public async Task<Result> ClearAllRequestsAsync() {
        _logger.LogInformation("ClearAllRequestsAsync: Clearing all pending requests");

        try {
            var result = await _storageService.RemoveItem<PendingBwAppRequests>(StorageArea.Session);
            if (result.IsFailed) {
                return Result.Fail(result.Errors);
            }

            _logger.LogDebug("ClearAllRequestsAsync: All requests cleared");
            return Result.Ok();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "ClearAllRequestsAsync: Failed to clear requests");
            return Result.Fail($"Failed to clear requests: {ex.Message}");
        }
    }

    public async Task<Result> CleanupStaleRequestsAsync(TimeSpan maxAge) {
        _logger.LogInformation("CleanupStaleRequestsAsync: Removing requests older than {MaxAge}", maxAge);

        try {
            var getResult = await _storageService.GetItem<PendingBwAppRequests>(StorageArea.Session);
            if (getResult.IsFailed) {
                return Result.Fail(getResult.Errors);
            }

            var current = getResult.Value ?? PendingBwAppRequests.Empty;
            var cleaned = current.WithoutStaleRequests(maxAge);

            if (cleaned.Count == current.Count) {
                _logger.LogDebug("CleanupStaleRequestsAsync: No stale requests found");
                return Result.Ok();
            }

            if (cleaned.IsEmpty) {
                var removeResult = await _storageService.RemoveItem<PendingBwAppRequests>(StorageArea.Session);
                if (removeResult.IsFailed) {
                    return Result.Fail(removeResult.Errors);
                }
            }
            else {
                var setResult = await _storageService.SetItem(cleaned, StorageArea.Session);
                if (setResult.IsFailed) {
                    return Result.Fail(setResult.Errors);
                }
            }

            _logger.LogInformation(
                "CleanupStaleRequestsAsync: Removed {Removed} stale requests, remaining={Remaining}",
                current.Count - cleaned.Count, cleaned.Count);

            return Result.Ok();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "CleanupStaleRequestsAsync: Failed to cleanup stale requests");
            return Result.Fail($"Failed to cleanup stale requests: {ex.Message}");
        }
    }

    public IDisposable Subscribe(IObserver<PendingBwAppRequests> observer) {
        lock (_observers) {
            if (!_observers.Contains(observer)) {
                _observers.Add(observer);
                _logger.LogDebug("Subscribe: Added observer, total={Count}", _observers.Count);
            }
        }

        // Fetch and send current state immediately
        Task.Run(async () => {
            try {
                var result = await GetRequestsAsync();
                if (result.IsSuccess) {
                    observer.OnNext(result.Value);
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Subscribe: Failed to send initial state to observer");
            }
        });

        return new Unsubscriber(this, observer);
    }

    private void NotifyObservers(PendingBwAppRequests requests) {
        // Notify event subscribers
        RequestsChanged?.Invoke(this, requests);

        // Notify IObservable subscribers
        List<IObserver<PendingBwAppRequests>> observersCopy;
        lock (_observers) {
            observersCopy = [.. _observers];
        }

        foreach (var observer in observersCopy) {
            try {
                observer.OnNext(requests);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "NotifyObservers: Observer threw exception");
            }
        }
    }

    private void RemoveObserver(IObserver<PendingBwAppRequests> observer) {
        lock (_observers) {
            _observers.Remove(observer);
            _logger.LogDebug("RemoveObserver: Removed observer, remaining={Count}", _observers.Count);
        }
    }

    public void Dispose() {
        _storageSubscription?.Dispose();
        _storageSubscription = null;

        lock (_observers) {
            foreach (var observer in _observers) {
                observer.OnCompleted();
            }
            _observers.Clear();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Internal observer that receives storage change notifications and relays to our observers.
    /// </summary>
    private sealed class StorageObserver(PendingBwAppRequestService service) : IObserver<PendingBwAppRequests> {
        public void OnCompleted() {
            // Storage observation ended
        }

        public void OnError(Exception error) {
            service._logger.LogError(error, "StorageObserver: Error received from storage");
        }

        public void OnNext(PendingBwAppRequests value) {
            service._logger.LogDebug("StorageObserver: Received update, count={Count}", value.Count);
            service.NotifyObservers(value);
        }
    }

    /// <summary>
    /// Unsubscriber for removing observers.
    /// </summary>
    private sealed class Unsubscriber(
        PendingBwAppRequestService service,
        IObserver<PendingBwAppRequests> observer
    ) : IDisposable {
        public void Dispose() {
            service.RemoveObserver(observer);
        }
    }
}
