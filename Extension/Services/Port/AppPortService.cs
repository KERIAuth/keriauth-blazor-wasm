using System.Collections.Concurrent;
using System.Text.Json;
using Extension.Helper;
using Extension.Models.Messages.AppBw;
using Extension.Models.Messages.BwApp;
using Extension.Models.Messages.Common;
using Extension.Models.Messages.Port;
using FluentResults;
using Microsoft.JSInterop;
using WebExtensions.Net;

namespace Extension.Services.Port;

/// <summary>
/// Service for managing port connection from App (popup/tab/sidepanel) to BackgroundWorker.
/// Implements IObservable&lt;BwAppMessage&gt; to allow reactive subscription to incoming BW messages.
/// </summary>
public class AppPortService : IAppPortService
{
    private readonly ILogger<AppPortService> _logger;
    private readonly IJSRuntime _jsRuntime;
    private readonly IWebExtensionsApi _webExtensionsApi;

    private WebExtensions.Net.Runtime.Port? _port;
    private readonly string _instanceId = Guid.NewGuid().ToString();
    private TaskCompletionSource<ReadyMessage>? _connectTcs;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RpcResponse>> _pendingRpcRequests = new();
    private readonly List<IObserver<BwAppMessage>> _observers = [];
    private bool _disposed;

    // Default timeout for RPC requests
    private static readonly TimeSpan DefaultRpcTimeout = TimeSpan.FromSeconds(30);

    public AppPortService(
        ILogger<AppPortService> logger,
        IJSRuntime jsRuntime,
        IWebExtensionsApi webExtensionsApi)
    {
        _logger = logger;
        _jsRuntime = jsRuntime;
        _webExtensionsApi = webExtensionsApi;
    }

    public bool IsConnected { get; private set; }
    public string? PortSessionId { get; private set; }
    public int? OriginTabId { get; private set; }
    public int? AttachedTabId { get; private set; }

    public event EventHandler<PortMessage>? MessageReceived;
    public event EventHandler? Disconnected;

    public async Task ConnectAsync()
    {
        if (IsConnected)
        {
            _logger.LogDebug("ConnectAsync called but already connected");
            return;
        }

        _logger.LogInformation("Connecting to BackgroundWorker via port...");

        // Create TaskCompletionSource for READY response
        _connectTcs = new TaskCompletionSource<ReadyMessage>();

        try
        {
            // Connect to BackgroundWorker using port name
            // Runtime.Connect returns Port directly (not Task<Port>)
            // Note: Must use ConnectInfo object - passing string directly is interpreted as extensionId
            var connectInfo = new WebExtensions.Net.Runtime.ConnectInfo { Name = "extension-app" };
            _port = _webExtensionsApi.Runtime.Connect(connectInfo: connectInfo);

            // Set up message listener with explicit delegate type to resolve ambiguity
            // WebExtensions.Net OnMessage expects: Func<object, MessageSender, Action<object>, bool>
            Func<object, WebExtensions.Net.Runtime.MessageSender, Action<object>, bool> onMessageHandler =
                (object message, WebExtensions.Net.Runtime.MessageSender sender, Action<object> sendResponse) =>
                {
                    _ = HandlePortMessageAsync(message);
                    return false; // Don't keep channel open
                };
            _port.OnMessage.AddListener(onMessageHandler);

            // Set up disconnect listener with explicit delegate type
            // WebExtensions.Net OnDisconnect expects: Action<Port>
            Action<WebExtensions.Net.Runtime.Port> onDisconnectHandler = (WebExtensions.Net.Runtime.Port disconnectedPort) =>
            {
                HandleDisconnect();
            };
            _port.OnDisconnect.AddListener(onDisconnectHandler);

            // Send HELLO message
            var helloMessage = new HelloMessage
            {
                Context = ContextKind.ExtensionApp,
                InstanceId = _instanceId,
                TabId = null,
                FrameId = null
            };

            await SendMessageInternalAsync(helloMessage);
            _logger.LogDebug("HELLO sent, waiting for READY...");

            // Wait for READY response with timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            cts.Token.Register(() => _connectTcs?.TrySetCanceled());

            var readyMessage = await _connectTcs.Task;

            PortSessionId = readyMessage.PortSessionId;
            OriginTabId = readyMessage.TabId;
            IsConnected = true;

            _logger.LogInformation("Connected to BackgroundWorker, portSessionId={PortSessionId}, originTabId={OriginTabId}",
                PortSessionId, OriginTabId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("ConnectAsync timed out waiting for READY");
            DisconnectInternal();
            throw new TimeoutException("Failed to receive READY from BackgroundWorker within timeout");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ConnectAsync failed");
            DisconnectInternal();
            throw;
        }
        finally
        {
            _connectTcs = null;
        }
    }

    /// <summary>
    /// Disconnects the port from the App side. Used for testing port disconnection scenarios.
    /// This mimics an unexpected disconnect by only calling the browser's Disconnect API
    /// and letting the OnDisconnect handler (HandleDisconnect) perform the state cleanup.
    /// </summary>
    public Task DisconnectAsync()
    {
        _logger.LogInformation("DisconnectAsync called");
        if (_port != null)
        {
            // Only call the browser Disconnect API - let OnDisconnect handler do cleanup
            _port.Disconnect();
        }
        return Task.CompletedTask;
    }

    public async Task AttachToTabAsync(int tabId, int? frameId = null)
    {
        if (!IsConnected || string.IsNullOrEmpty(PortSessionId))
        {
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        }

        _logger.LogInformation("Attaching to tab {TabId}, frameId={FrameId}", tabId, frameId);

        var attachMessage = new AttachTabMessage
        {
            TabId = tabId,
            FrameId = frameId
        };

        // Send ATTACH_TAB - BW doesn't respond with RPC_RES for this, just send and assume success
        // If there's an error, BW will send an ERROR message
        await SendMessageAsync(attachMessage);

        AttachedTabId = tabId;
        _logger.LogInformation("Attached to tab {TabId}", tabId);
    }

    public async Task DetachFromTabAsync()
    {
        if (!IsConnected || AttachedTabId == null)
        {
            _logger.LogDebug("DetachFromTabAsync: Not attached to any tab");
            return;
        }

        _logger.LogInformation("Detaching from tab {TabId}", AttachedTabId);

        var detachMessage = new DetachTabMessage();
        await SendMessageAsync(detachMessage);

        AttachedTabId = null;
    }

    public async Task SendMessageAsync(PortMessage message)
    {
        if (!IsConnected || _port == null)
        {
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        }

        await SendMessageInternalAsync(message);
    }

    public async Task<RpcResponse> SendRpcRequestAsync(string method, object? parameters = null, TimeSpan? timeout = null)
    {
        if (!IsConnected || string.IsNullOrEmpty(PortSessionId))
        {
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        }

        var requestId = Guid.NewGuid().ToString();
        var rpcRequest = new RpcRequest
        {
            PortSessionId = PortSessionId,
            Id = requestId,
            Method = method,
            Params = parameters
        };

        return await SendRpcRequestInternalAsync(rpcRequest, timeout ?? DefaultRpcTimeout);
    }

    public async Task SendEventAsync(string eventName, object? data = null)
    {
        if (!IsConnected || string.IsNullOrEmpty(PortSessionId))
        {
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        }

        var eventMessage = new EventMessage
        {
            PortSessionId = PortSessionId,
            Name = eventName,
            Data = data
        };

        await SendMessageAsync(eventMessage);
    }

    private async Task<RpcResponse> SendRpcRequestInternalAsync(RpcRequest rpcRequest, TimeSpan timeout)
    {
        var requestId = rpcRequest.Id;

        var tcs = new TaskCompletionSource<RpcResponse>();
        _pendingRpcRequests[requestId] = tcs;

        try
        {
            await SendMessageInternalAsync(rpcRequest);

            using var cts = new CancellationTokenSource(timeout);
            cts.Token.Register(() =>
            {
                if (_pendingRpcRequests.TryRemove(requestId, out var removed))
                {
                    removed.TrySetCanceled();
                }
            });

            return await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("RPC request timed out: {RequestId}", requestId);
            throw new TimeoutException($"RPC request timed out: {requestId}");
        }
        finally
        {
            _pendingRpcRequests.TryRemove(requestId, out _);
        }
    }

    private Task SendMessageInternalAsync<T>(T message) where T : PortMessage
    {
        if (_port == null)
        {
            throw new InvalidOperationException("Port is not connected");
        }

        try
        {
            // Use the Port's built-in PostMessage method from WebExtensions.Net
            // PostMessage is synchronous - it queues the message for delivery
            _port.PostMessage(message);
            _logger.LogDebug("Sent port message: type={Type}", message.T);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send port message: type={Type}", message.T);
            throw;
        }
    }

    private async Task HandlePortMessageAsync(object messageObj)
    {
        try
        {
            var messageJson = JsonSerializer.Serialize(messageObj, JsonOptions.CamelCase);
            var baseMsg = JsonSerializer.Deserialize<JsonElement>(messageJson, JsonOptions.CamelCase);

            if (!baseMsg.TryGetProperty("t", out var typeElement))
            {
                _logger.LogWarning("Port message missing 't' property");
                return;
            }

            var messageType = typeElement.GetString();
            _logger.LogDebug("Port message received: type={Type}", messageType);

            switch (messageType)
            {
                case "READY":
                    HandleReadyMessage(messageJson);
                    break;
                case "RPC_RES":
                    HandleRpcResponse(messageJson);
                    break;
                case "EVENT":
                    HandleEventMessage(messageJson);
                    break;
                case "ERROR":
                    HandleErrorMessage(messageJson);
                    break;
                default:
                    _logger.LogWarning("Unknown port message type: {Type}", messageType);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling port message");
        }
    }

    private void HandleReadyMessage(string messageJson)
    {
        try
        {
            var readyMessage = JsonSerializer.Deserialize<ReadyMessage>(messageJson, JsonOptions.CamelCase);
            if (readyMessage == null)
            {
                _logger.LogError("Failed to deserialize READY message");
                return;
            }

            _logger.LogDebug("READY received: portSessionId={PortSessionId}, tabId={TabId}",
                readyMessage.PortSessionId, readyMessage.TabId);

            // Complete the connect task
            _connectTcs?.TrySetResult(readyMessage);

            // Raise event
            MessageReceived?.Invoke(this, readyMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling READY message");
            _connectTcs?.TrySetException(ex);
        }
    }

    private void HandleRpcResponse(string messageJson)
    {
        try
        {
            var rpcResponse = JsonSerializer.Deserialize<RpcResponse>(messageJson, JsonOptions.CamelCase);
            if (rpcResponse == null)
            {
                _logger.LogError("Failed to deserialize RPC_RES message");
                return;
            }

            _logger.LogDebug("RPC_RES received: id={Id}, error={Error}",
                rpcResponse.Id, rpcResponse.Error);

            // Complete the pending request
            if (_pendingRpcRequests.TryRemove(rpcResponse.Id, out var tcs))
            {
                tcs.TrySetResult(rpcResponse);
            }
            else
            {
                _logger.LogWarning("No pending request found for id={Id}", rpcResponse.Id);
            }

            // Raise event
            MessageReceived?.Invoke(this, rpcResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling RPC_RES message");
        }
    }

    private void HandleEventMessage(string messageJson)
    {
        try
        {
            var eventMessage = JsonSerializer.Deserialize<EventMessage>(messageJson, JsonOptions.CamelCase);
            if (eventMessage == null)
            {
                _logger.LogError("Failed to deserialize EVENT message");
                return;
            }

            _logger.LogDebug("EVENT received: name={Name}", eventMessage.Name);

            // Raise event for PortMessage subscribers
            MessageReceived?.Invoke(this, eventMessage);

            // Convert to BwAppMessage and notify IObservable subscribers
            // This allows components using reactive patterns to receive BW messages
            var bwAppMessage = new BwAppMessage(
                eventMessage.Name,
                null, // EVENT messages don't have a requestId
                eventMessage.Data,
                null  // No error
            );
            NotifyObservers(bwAppMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling EVENT message");
        }
    }

    private void HandleErrorMessage(string messageJson)
    {
        try
        {
            var errorMessage = JsonSerializer.Deserialize<ErrorMessage>(messageJson, JsonOptions.CamelCase);
            if (errorMessage == null)
            {
                _logger.LogError("Failed to deserialize ERROR message");
                return;
            }

            _logger.LogWarning("ERROR received: code={Code}, message={Message}",
                errorMessage.Code, errorMessage.Message);

            // Raise event
            MessageReceived?.Invoke(this, errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling ERROR message");
        }
    }

    private void HandleDisconnect()
    {
        _logger.LogInformation("Port disconnected");

        IsConnected = false;
        PortSessionId = null;
        OriginTabId = null;
        AttachedTabId = null;
        _port = null;

        // Cancel any pending requests
        foreach (var kvp in _pendingRpcRequests)
        {
            kvp.Value.TrySetCanceled();
        }
        _pendingRpcRequests.Clear();

        // Cancel connect if in progress
        _connectTcs?.TrySetCanceled();

        // Raise event
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Internal disconnect used during connection errors and disposal.
    /// Directly cleans up state without going through OnDisconnect handler.
    /// Use DisconnectAsync() for testing to mimic unexpected disconnects.
    /// </summary>
    private void DisconnectInternal()
    {
        if (_port != null)
        {
            try
            {
                _port.Disconnect();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error during port disconnect");
            }
            _port = null;
        }

        IsConnected = false;
        PortSessionId = null;
        OriginTabId = null;
        AttachedTabId = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        // Notify observers of completion
        foreach (var observer in _observers.ToArray())
        {
            observer.OnCompleted();
        }
        _observers.Clear();

        DisconnectInternal();
        await Task.CompletedTask; // Satisfy async requirement
        GC.SuppressFinalize(this);
    }

    #region IObservable<BwAppMessage> Implementation

    /// <summary>
    /// Subscribe to receive BwAppMessage notifications from BackgroundWorker.
    /// </summary>
    public IDisposable Subscribe(IObserver<BwAppMessage> observer)
    {
        if (!_observers.Contains(observer))
        {
            _observers.Add(observer);
        }
        return new Unsubscriber(_observers, observer);
    }

    /// <summary>
    /// Notify all observers of a new BwAppMessage.
    /// </summary>
    private void NotifyObservers(BwAppMessage message)
    {
        _logger.LogDebug("Notifying {Count} observers of message type: {Type}", _observers.Count, message.Type);
        foreach (var observer in _observers.ToArray())
        {
            observer.OnNext(message);
        }
    }

    /// <summary>
    /// Helper class for managing observer unsubscription.
    /// </summary>
    private sealed class Unsubscriber(List<IObserver<BwAppMessage>> observers, IObserver<BwAppMessage> observer) : IDisposable
    {
        public void Dispose()
        {
            if (observer != null && observers.Contains(observer))
            {
                observers.Remove(observer);
            }
        }
    }

    #endregion

    #region AppBwMessage Compatibility Methods

    /// <summary>
    /// Sends a strongly-typed message from App to BackgroundWorker (fire-and-forget).
    /// Converts the AppBwMessage to an RPC request format.
    /// </summary>
    public async Task SendToBackgroundWorkerAsync<TPayload>(AppBwMessage<TPayload> message)
    {
        _logger.LogInformation("SendToBackgroundWorkerAsync: type={Type}, tabId={TabId}",
            message.Type, message.TabId);

        if (!IsConnected || string.IsNullOrEmpty(PortSessionId))
        {
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        }

        // Convert AppBwMessage to RPC request format
        // The method is the message type, params contains the full message
        var rpcParams = new
        {
            type = message.Type,
            requestId = message.RequestId,
            tabId = message.TabId,
            tabUrl = message.TabUrl,
            payload = message.Payload,
            error = message.Error
        };

        try
        {
            // Send as fire-and-forget RPC (don't await the response)
            await SendRpcRequestAsync(message.Type, rpcParams);
            _logger.LogInformation("SendToBackgroundWorkerAsync sent: type={Type}", message.Type);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendToBackgroundWorkerAsync failed: type={Type}", message.Type);
            throw;
        }
    }

    /// <summary>
    /// Sends a strongly-typed message from App to BackgroundWorker and awaits a response.
    /// Converts the AppBwMessage to an RPC request and deserializes the response.
    /// </summary>
    public async Task<Result<TResponse?>> SendRequestAsync<TPayload, TResponse>(
        AppBwMessage<TPayload> message,
        TimeSpan? timeout = null) where TResponse : class, IResponseMessage
    {

        timeout ??= DefaultRpcTimeout;

        _logger.LogInformation("SendRequestAsync: type={Type}, tabId={TabId}, timeout={Timeout}",
            message.Type, message.TabId, timeout);

        if (!IsConnected || string.IsNullOrEmpty(PortSessionId))
        {
            return Result.Fail<TResponse?>("Not connected. Call ConnectAsync first.");
        }

        try
        {
            // Convert AppBwMessage to RPC request format
            var rpcParams = new
            {
                type = message.Type,
                requestId = message.RequestId,
                tabId = message.TabId,
                tabUrl = message.TabUrl,
                payload = message.Payload
            };

            var response = await SendRpcRequestAsync(message.Type, rpcParams, timeout);

            if (!response.Ok || response.Error is not null)
            {
                _logger.LogWarning("SendRequestAsync failed: error={Error}", response.Error);
                return Result.Fail<TResponse?>(response.Error ?? "RPC request failed");
            }

            // Deserialize the result to the expected response type
            if (response.Result is null)
            {
                _logger.LogWarning("SendRequestAsync received null result");
                return Result.Fail<TResponse?>("Received null result from BackgroundWorker");
            }

            // Handle JsonElement result from deserialization
            if (response.Result is JsonElement jsonElement)
            {
                var responseJson = jsonElement.GetRawText();
                var typedResponse = JsonSerializer.Deserialize<TResponse>(responseJson, JsonOptions.PortMessaging);
                _logger.LogInformation("SendRequestAsync deserialized response: {Type}", typeof(TResponse).Name);
                return Result.Ok(typedResponse);
            }

            // Try to serialize and deserialize for other object types
            var resultJson = JsonSerializer.Serialize(response.Result, JsonOptions.PortMessaging);
            var deserializedResponse = JsonSerializer.Deserialize<TResponse>(resultJson, JsonOptions.PortMessaging);
            return Result.Ok(deserializedResponse);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("SendRequestAsync timed out after {Timeout}", timeout);
            return Result.Fail<TResponse?>($"Request timed out after {timeout.Value.TotalSeconds} seconds");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "SendRequestAsync failed to deserialize response");
            return Result.Fail<TResponse?>($"Failed to deserialize response: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendRequestAsync failed");
            return Result.Fail<TResponse?>($"Request failed: {ex.Message}");
        }
    }

    #endregion
}
