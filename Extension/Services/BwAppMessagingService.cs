using System.Collections.Concurrent;
using System.Text.Json;
using Extension.Models.Messages.BwApp;
using Extension.Models.Messages.Common;
using Extension.Models.Storage;
using FluentResults;
using Microsoft.Extensions.Logging;
using WebExtensions.Net;

namespace Extension.Services;

/// <summary>
/// Service for BackgroundWorker to send messages and requests to App (popup/tab/sidepanel).
/// Direction: BackgroundWorker → App
///
/// Request flow:
/// 1. BW calls SendRequestToAppAsync() with a message
/// 2. Service stores request in session storage (persists across service worker restarts)
/// 3. Service also sends runtime message to App (for immediate notification if App is open)
/// 4. App detects request via storage subscription or runtime message
/// 5. App processes request and sends response via AppBwResponseToBwRequestMessage
/// 6. BW receives response, correlates via requestId, cleans up storage
/// </summary>
public class BwAppMessagingService : IBwAppMessagingService {
    private readonly IWebExtensionsApi _webExtensionsApi;
    private readonly IPendingBwAppRequestService _pendingRequestService;
    private readonly ILogger<BwAppMessagingService> _logger;

    /// <summary>
    /// Pending requests awaiting responses from App.
    /// Key: requestId, Value: TaskCompletionSource for the response
    /// Note: This is in-memory tracking for correlating responses. The actual request
    /// is persisted in session storage via IPendingBwAppRequestService.
    /// </summary>
    private readonly ConcurrentDictionary<string, PendingRequest> _pendingRequests = new();

    private static readonly JsonSerializerOptions MessageJsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        MaxDepth = 64
    };

    public BwAppMessagingService(
        IWebExtensionsApi webExtensionsApi,
        IPendingBwAppRequestService pendingRequestService,
        ILogger<BwAppMessagingService> logger
    ) {
        _webExtensionsApi = webExtensionsApi;
        _pendingRequestService = pendingRequestService;
        _logger = logger;
    }

    /// <summary>
    /// Sends a fire-and-forget message from BackgroundWorker to App.
    /// </summary>
    public async Task SendToAppAsync<TPayload>(BwAppMessage<TPayload> message) {
        _logger.LogInformation("SendToAppAsync: type={Type}, requestId={RequestId}",
            message.Type, message.RequestId);

        try {
            var messageJson = JsonSerializer.Serialize(message, MessageJsonOptions);
            var messageToSend = JsonSerializer.Deserialize<object>(messageJson, MessageJsonOptions);

            await _webExtensionsApi.Runtime.SendMessage(messageToSend);

            _logger.LogDebug("SendToAppAsync: Message sent successfully");
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "SendToAppAsync: Failed to send message type={Type}", message.Type);
            // Fire-and-forget, so we don't throw
        }
    }

    /// <summary>
    /// Sends a request from BackgroundWorker to App and awaits a response.
    /// Returns Result.Fail on timeout, if App closes, or on communication error.
    ///
    /// The request is persisted to session storage so App can detect it via storage subscription
    /// even if App wasn't open when the request was sent. This enables:
    /// - Service worker restart resilience
    /// - App opening after request was queued
    /// - Multiple App contexts (Popup, SidePanel, Tab) detecting the same request
    /// </summary>
    public async Task<Result<TResponse?>> SendRequestToAppAsync<TPayload, TResponse>(
        BwAppMessage<TPayload> message,
        TimeSpan? timeout = null) where TResponse : class, IResponseMessage {
        timeout ??= AppConfig.DefaultRequestTimeout;

        // Generate a unique request ID if not provided
        var requestId = message.RequestId ?? Guid.NewGuid().ToString();

        // Create message with request ID
        var requestMessage = message with { RequestId = requestId };

        _logger.LogInformation(
            "SendRequestToAppAsync: type={Type}, requestId={RequestId}, timeout={Timeout}",
            requestMessage.Type, requestId, timeout);

        // Create TaskCompletionSource for awaiting the response
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingRequest = new PendingRequest(tcs, typeof(TResponse), DateTime.UtcNow.Add(timeout.Value));

        if (!_pendingRequests.TryAdd(requestId, pendingRequest)) {
            return Result.Fail<TResponse?>($"Request ID '{requestId}' is already pending");
        }

        try {
            // Store request in session storage for App to detect via subscription
            // This persists across service worker restarts and allows App to see pending
            // requests even if App opens after the request was queued
            var storageRequest = new PendingBwAppRequest {
                RequestId = requestId,
                Type = requestMessage.Type,
                Payload = requestMessage.Payload,
                CreatedAtUtc = DateTime.UtcNow,
                TabId = null, // Can be set if request is tab-specific
                TabUrl = null
            };

            var addResult = await _pendingRequestService.AddRequestAsync(storageRequest);
            if (addResult.IsFailed) {
                var errorMessage = addResult.Errors.Count > 0 ? addResult.Errors[0].Message : "Unknown error";
                _logger.LogWarning(
                    "SendRequestToAppAsync: Failed to store request in session storage, requestId={RequestId}. Error: {Error}",
                    requestId, errorMessage);
                // Continue anyway - runtime message may still work if App is open
            }

            // Also send runtime message for immediate notification if App is open
            // App may receive both storage change notification and runtime message;
            // it should deduplicate based on requestId
            var messageJson = JsonSerializer.Serialize(requestMessage, MessageJsonOptions);
            var messageToSend = JsonSerializer.Deserialize<object>(messageJson, MessageJsonOptions);

            try {
                await _webExtensionsApi.Runtime.SendMessage(messageToSend);
                _logger.LogDebug("SendRequestToAppAsync: Runtime message sent");
            }
            catch (Exception ex) {
                // Runtime.SendMessage fails if no listener is connected (App not open)
                // This is expected - App will detect request via storage subscription when it opens
                _logger.LogDebug(ex,
                    "SendRequestToAppAsync: Runtime message failed (App may not be open), requestId={RequestId}",
                    requestId);
            }

            _logger.LogDebug("SendRequestToAppAsync: Request queued, waiting for response");

            // Wait for response with timeout
            using var cts = new CancellationTokenSource(timeout.Value);
            var delayTask = Task.Delay(timeout.Value, cts.Token);
            var completedTask = await Task.WhenAny(tcs.Task, delayTask);

            if (completedTask != tcs.Task) {
                _logger.LogWarning("SendRequestToAppAsync: Timed out waiting for response, requestId={RequestId}", requestId);
                return Result.Fail<TResponse?>($"Request timed out after {timeout.Value.TotalSeconds} seconds waiting for App response");
            }

            // Cancel the delay task
            await cts.CancelAsync();

            var response = await tcs.Task;

            if (response is null) {
                _logger.LogWarning("SendRequestToAppAsync: Received null response, requestId={RequestId}", requestId);
                return Result.Fail<TResponse?>("Received null response from App");
            }

            // Deserialize the response
            var responseJson = JsonSerializer.Serialize(response, MessageJsonOptions);
            var typedResponse = JsonSerializer.Deserialize<TResponse>(responseJson, MessageJsonOptions);

            _logger.LogInformation("SendRequestToAppAsync: Received response, requestId={RequestId}", requestId);
            return Result.Ok(typedResponse);
        }
        catch (TaskCanceledException) {
            _logger.LogWarning("SendRequestToAppAsync: Request was cancelled, requestId={RequestId}", requestId);
            return Result.Fail<TResponse?>("Request was cancelled");
        }
        catch (JsonException ex) {
            _logger.LogError(ex, "SendRequestToAppAsync: Failed to deserialize response, requestId={RequestId}", requestId);
            return Result.Fail<TResponse?>($"Failed to deserialize response: {ex.Message}");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "SendRequestToAppAsync: Request failed, requestId={RequestId}", requestId);
            return Result.Fail<TResponse?>($"Request failed: {ex.Message}");
        }
        finally {
            // Clean up: remove from in-memory tracking
            _pendingRequests.TryRemove(requestId, out _);

            // Clean up: remove from session storage
            var removeResult = await _pendingRequestService.RemoveRequestAsync(requestId);
            if (removeResult.IsFailed) {
                _logger.LogWarning(
                    "SendRequestToAppAsync: Failed to remove request from storage, requestId={RequestId}",
                    requestId);
            }
        }
    }

    /// <summary>
    /// Handles a response received from App for a pending request.
    /// Called when App sends a response message back to BackgroundWorker.
    /// </summary>
    public void HandleResponseFromApp(string requestId, object? response) {
        _logger.LogInformation("HandleResponseFromApp: requestId={RequestId}", requestId);

        if (_pendingRequests.TryRemove(requestId, out var pendingRequest)) {
            pendingRequest.TaskCompletionSource.TrySetResult(response);
            _logger.LogDebug("HandleResponseFromApp: Response delivered for requestId={RequestId}", requestId);
        }
        else {
            _logger.LogWarning("HandleResponseFromApp: No pending request found for requestId={RequestId}", requestId);
        }
    }

    /// <summary>
    /// Handles App closure, failing any pending requests that were waiting for a response.
    /// </summary>
    public void HandleAppClosed(int? tabId = null) {
        _logger.LogInformation("HandleAppClosed: tabId={TabId}, pending requests={Count}",
            tabId, _pendingRequests.Count);

        // Fail all pending requests (in a real implementation, you might filter by tabId)
        foreach (var kvp in _pendingRequests) {
            if (_pendingRequests.TryRemove(kvp.Key, out var pendingRequest)) {
                pendingRequest.TaskCompletionSource.TrySetException(
                    new OperationCanceledException("App was closed before responding"));
                _logger.LogDebug("HandleAppClosed: Failed pending request requestId={RequestId}", kvp.Key);
            }
        }
    }

    /// <summary>
    /// Internal record for tracking pending requests.
    /// </summary>
    private sealed record PendingRequest(
        TaskCompletionSource<object?> TaskCompletionSource,
        Type ExpectedResponseType,
        DateTime ExpiresAtUtc
    );
}
