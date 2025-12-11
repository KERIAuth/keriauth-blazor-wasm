namespace Extension.Services;

using Extension.Models.Storage;
using FluentResults;

/// <summary>
/// Service for managing pending requests from BackgroundWorker to App.
/// Direction: BackgroundWorker → App
///
/// Usage:
/// - BackgroundWorker: Call AddRequestAsync() to queue a request, await response via BwAppMessagingService
/// - App: Subscribe to OnRequestsChanged, process requests, send response via AppBwResponseToBwRequestMessage
///
/// Storage: Uses chrome.storage.session for persistence across service worker restarts.
/// </summary>
public interface IPendingBwAppRequestService {
    /// <summary>
    /// Adds a pending request to the queue.
    /// Called by BackgroundWorker when initiating a request to App.
    /// </summary>
    /// <param name="request">The request to add</param>
    /// <returns>Result indicating success or failure</returns>
    Task<Result> AddRequestAsync(PendingBwAppRequest request);

    /// <summary>
    /// Removes a pending request from the queue by ID.
    /// Called when:
    /// - App has processed the request and sent a response
    /// - Request has timed out
    /// - Request was cancelled
    /// </summary>
    /// <param name="requestId">The request ID to remove</param>
    /// <returns>Result indicating success or failure</returns>
    Task<Result> RemoveRequestAsync(string requestId);

    /// <summary>
    /// Gets all pending requests.
    /// </summary>
    /// <returns>Result containing the pending requests collection</returns>
    Task<Result<PendingBwAppRequests>> GetRequestsAsync();

    /// <summary>
    /// Gets a specific pending request by ID.
    /// </summary>
    /// <param name="requestId">The request ID to find</param>
    /// <returns>Result containing the request if found, null if not found</returns>
    Task<Result<PendingBwAppRequest?>> GetRequestAsync(string requestId);

    /// <summary>
    /// Clears all pending requests.
    /// Useful for cleanup on App close or error recovery.
    /// </summary>
    /// <returns>Result indicating success or failure</returns>
    Task<Result> ClearAllRequestsAsync();

    /// <summary>
    /// Removes requests older than the specified age.
    /// Useful for periodic cleanup of stale requests.
    /// </summary>
    /// <param name="maxAge">Maximum age of requests to keep</param>
    /// <returns>Result indicating success or failure</returns>
    Task<Result> CleanupStaleRequestsAsync(TimeSpan maxAge);

    /// <summary>
    /// Subscribes to changes in the pending requests collection.
    /// Called by App (Popup/SidePanel/Tab) to be notified when new requests arrive.
    /// </summary>
    /// <param name="observer">Observer to notify of changes</param>
    /// <returns>Disposable subscription - dispose to unsubscribe</returns>
    IDisposable Subscribe(IObserver<PendingBwAppRequests> observer);

    /// <summary>
    /// Event raised when the pending requests collection changes.
    /// Alternative to Subscribe() for simpler event-based notification.
    /// </summary>
    event EventHandler<PendingBwAppRequests>? RequestsChanged;
}
