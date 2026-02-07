using Extension.Models.Messages.AppBw;
using Extension.Models.Messages.BwApp;
using Extension.Models.Messages.Common;
using Extension.Models.Messages.Port;
using FluentResults;

namespace Extension.Services.Port;

/// <summary>
/// Service interface for managing port connection from App (popup/tab/sidepanel) to BackgroundWorker.
/// Implements IObservable&lt;BwAppMessage&gt; to allow reactive subscription to incoming BW messages.
/// </summary>
public interface IAppPortService : IAsyncDisposable, IObservable<BwAppMessage> {
    /// <summary>
    /// Connects to the BackgroundWorker and completes the HELLO/READY handshake.
    /// </summary>
    /// <returns>Task that completes when the connection is established and READY is received.</returns>
    Task ConnectAsync();

    /// <summary>
    /// Attaches this App to a specific tab's PortSession.
    /// Must be called after ConnectAsync().
    /// </summary>
    /// <param name="tabId">The tab ID to attach to.</param>
    /// <param name="frameId">The frame ID (optional, defaults to main frame).</param>
    Task AttachToTabAsync(int tabId, int? frameId = null);

    /// <summary>
    /// Detaches this App from its currently attached tab's PortSession.
    /// </summary>
    Task DetachFromTabAsync();

    /// <summary>
    /// Sends a message to the BackgroundWorker via the port.
    /// </summary>
    /// <param name="message">The message to send.</param>
    Task SendMessageAsync(PortMessage message);

    /// <summary>
    /// Sends an RPC request and waits for the response.
    /// </summary>
    /// <param name="method">The RPC method name.</param>
    /// <param name="parameters">Optional parameters for the method.</param>
    /// <param name="timeout">Optional timeout (defaults to AppConfig.DefaultRequestTimeout).</param>
    /// <returns>The RPC response.</returns>
    Task<RpcResponse> SendRpcRequestAsync(string method, object? parameters = null, TimeSpan? timeout = null);

    /// <summary>
    /// Sends an event to the BackgroundWorker (fire-and-forget).
    /// </summary>
    /// <param name="eventName">The event name.</param>
    /// <param name="data">Optional event data.</param>
    Task SendEventAsync(string eventName, object? data = null);

    /// <summary>
    /// Gets whether the port is connected and READY has been received.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets the PortSessionId received from the READY message.
    /// Null if not yet connected.
    /// </summary>
    string? PortSessionId { get; }

    /// <summary>
    /// Gets the TabId received from the READY message (for popup context).
    /// Null if not yet connected or if App is not tab-scoped.
    /// </summary>
    int? OriginTabId { get; }

    /// <summary>
    /// Gets the TabId this App is currently attached to.
    /// Null if not attached to any tab.
    /// </summary>
    int? AttachedTabId { get; }

    /// <summary>
    /// Event raised when a message is received from the BackgroundWorker.
    /// </summary>
    event EventHandler<PortMessage>? MessageReceived;

    /// <summary>
    /// Event raised when the port is disconnected.
    /// </summary>
    event EventHandler? Disconnected;

    /// <summary>
    /// Sends a strongly-typed message from App to BackgroundWorker (fire-and-forget).
    /// This is a convenience wrapper that converts AppBwMessage to RPC format.
    /// </summary>
    /// <typeparam name="TPayload">The payload type for the message.</typeparam>
    /// <param name="message">The message to send.</param>
    Task SendToBackgroundWorkerAsync<TPayload>(AppBwMessage<TPayload> message);

    /// <summary>
    /// Sends a strongly-typed message from App to BackgroundWorker and awaits a response.
    /// This is a convenience wrapper that converts AppBwMessage to RPC format.
    /// </summary>
    /// <typeparam name="TPayload">The payload type for the request message.</typeparam>
    /// <typeparam name="TResponse">The expected response type from BackgroundWorker.</typeparam>
    /// <param name="message">The request message to send.</param>
    /// <param name="timeout">Optional timeout (defaults to 30 seconds).</param>
    /// <returns>Result containing the response or failure information.</returns>
    Task<Result<TResponse?>> SendRequestAsync<TPayload, TResponse>(
        AppBwMessage<TPayload> message,
        TimeSpan? timeout = null) where TResponse : class, IResponseMessage;
}
