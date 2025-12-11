using Extension.Models.Messages.BwApp;
using Extension.Models.Messages.Common;
using FluentResults;

namespace Extension.Services;

/// <summary>
/// Service interface for BackgroundWorker to send messages and requests to App (popup/tab/sidepanel).
/// Direction: BackgroundWorker → App
/// </summary>
public interface IBwAppMessagingService
{
    /// <summary>
    /// Sends a fire-and-forget message from BackgroundWorker to App.
    /// </summary>
    /// <typeparam name="TPayload">The payload type for the message</typeparam>
    /// <param name="message">The message to send</param>
    Task SendToAppAsync<TPayload>(BwAppMessage<TPayload> message);

    /// <summary>
    /// Sends a request from BackgroundWorker to App and awaits a response.
    /// Returns Result.Fail on timeout, if App closes, or on communication error.
    /// </summary>
    /// <typeparam name="TPayload">The payload type for the request message</typeparam>
    /// <typeparam name="TResponse">The expected response type from App</typeparam>
    /// <param name="message">The request message to send</param>
    /// <param name="timeout">Optional timeout (defaults to AppConfig.DefaultRequestTimeout)</param>
    /// <returns>Result containing the response or failure information</returns>
    Task<Result<TResponse?>> SendRequestToAppAsync<TPayload, TResponse>(
        BwAppMessage<TPayload> message,
        TimeSpan? timeout = null) where TResponse : class, IResponseMessage;

    /// <summary>
    /// Handles a response received from App for a pending request.
    /// Called when App sends a response message back to BackgroundWorker.
    /// </summary>
    /// <param name="requestId">The request ID being responded to</param>
    /// <param name="response">The response object</param>
    void HandleResponseFromApp(string requestId, object? response);

    /// <summary>
    /// Handles App closure, failing any pending requests that were waiting for a response.
    /// Called when App sends an AppClosed message.
    /// </summary>
    /// <param name="tabId">Optional tab ID of the closed App instance</param>
    void HandleAppClosed(int? tabId = null);
}
