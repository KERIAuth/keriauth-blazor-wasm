using System.Collections.Concurrent;
using System.Text.Json;
using Extension.Models.Messages.Port;
using Microsoft.JSInterop;
using WebExtensions.Net;

namespace Extension.Services.Port;

/// <summary>
/// Service for managing port connection from App (popup/tab/sidepanel) to BackgroundWorker.
/// </summary>
public class AppPortService : IAppPortService {
    private readonly ILogger<AppPortService> _logger;
    private readonly IJSRuntime _jsRuntime;
    private readonly IWebExtensionsApi _webExtensionsApi;

    private WebExtensions.Net.Runtime.Port? _port;
    private readonly string _instanceId = Guid.NewGuid().ToString();
    private TaskCompletionSource<ReadyMessage>? _connectTcs;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RpcResponse>> _pendingRpcRequests = new();
    private bool _disposed;

    // JSON serialization options
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // Default timeout for RPC requests
    private static readonly TimeSpan DefaultRpcTimeout = TimeSpan.FromSeconds(30);

    public AppPortService(
        ILogger<AppPortService> logger,
        IJSRuntime jsRuntime,
        IWebExtensionsApi webExtensionsApi) {
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

    public async Task ConnectAsync() {
        if (IsConnected) {
            _logger.LogDebug("ConnectAsync called but already connected");
            return;
        }

        _logger.LogInformation("Connecting to BackgroundWorker via port...");

        // Create TaskCompletionSource for READY response
        _connectTcs = new TaskCompletionSource<ReadyMessage>();

        try {
            // Connect to BackgroundWorker using port name
            // Runtime.Connect returns Port directly (not Task<Port>)
            _port = _webExtensionsApi.Runtime.Connect("extension-app");

            // Set up message listener with explicit delegate type to resolve ambiguity
            // WebExtensions.Net OnMessage expects: Func<object, MessageSender, Action<object>, bool>
            Func<object, WebExtensions.Net.Runtime.MessageSender, Action<object>, bool> onMessageHandler =
                (object message, WebExtensions.Net.Runtime.MessageSender sender, Action<object> sendResponse) => {
                    _ = HandlePortMessageAsync(message);
                    return false; // Don't keep channel open
                };
            _port.OnMessage.AddListener(onMessageHandler);

            // Set up disconnect listener with explicit delegate type
            // WebExtensions.Net OnDisconnect expects: Action<Port>
            Action<WebExtensions.Net.Runtime.Port> onDisconnectHandler = (WebExtensions.Net.Runtime.Port disconnectedPort) => {
                HandleDisconnect();
            };
            _port.OnDisconnect.AddListener(onDisconnectHandler);

            // Send HELLO message
            var helloMessage = new HelloMessage {
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
        catch (OperationCanceledException) {
            _logger.LogError("ConnectAsync timed out waiting for READY");
            DisconnectInternal();
            throw new TimeoutException("Failed to receive READY from BackgroundWorker within timeout");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "ConnectAsync failed");
            DisconnectInternal();
            throw;
        }
        finally {
            _connectTcs = null;
        }
    }

    public async Task AttachToTabAsync(int tabId, int? frameId = null) {
        if (!IsConnected || string.IsNullOrEmpty(PortSessionId)) {
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        }

        _logger.LogInformation("Attaching to tab {TabId}, frameId={FrameId}", tabId, frameId);

        var attachMessage = new AttachTabMessage {
            TabId = tabId,
            FrameId = frameId
        };

        // Send ATTACH_TAB - BW doesn't respond with RPC_RES for this, just send and assume success
        // If there's an error, BW will send an ERROR message
        await SendMessageAsync(attachMessage);

        AttachedTabId = tabId;
        _logger.LogInformation("Attached to tab {TabId}", tabId);
    }

    public async Task DetachFromTabAsync() {
        if (!IsConnected || AttachedTabId == null) {
            _logger.LogDebug("DetachFromTabAsync: Not attached to any tab");
            return;
        }

        _logger.LogInformation("Detaching from tab {TabId}", AttachedTabId);

        var detachMessage = new DetachTabMessage();
        await SendMessageAsync(detachMessage);

        AttachedTabId = null;
    }

    public async Task SendMessageAsync(PortMessage message) {
        if (!IsConnected || _port == null) {
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        }

        await SendMessageInternalAsync(message);
    }

    public async Task<RpcResponse> SendRpcRequestAsync(string method, object? parameters = null, TimeSpan? timeout = null) {
        if (!IsConnected || string.IsNullOrEmpty(PortSessionId)) {
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        }

        var requestId = Guid.NewGuid().ToString();
        var rpcRequest = new RpcRequest {
            PortSessionId = PortSessionId,
            Id = requestId,
            Method = method,
            Params = parameters
        };

        return await SendRpcRequestInternalAsync(rpcRequest, timeout ?? DefaultRpcTimeout);
    }

    public async Task SendEventAsync(string eventName, object? data = null) {
        if (!IsConnected || string.IsNullOrEmpty(PortSessionId)) {
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        }

        var eventMessage = new EventMessage {
            PortSessionId = PortSessionId,
            Name = eventName,
            Data = data
        };

        await SendMessageAsync(eventMessage);
    }

    private async Task<RpcResponse> SendRpcRequestInternalAsync(RpcRequest rpcRequest, TimeSpan timeout) {
        var requestId = rpcRequest.Id;

        var tcs = new TaskCompletionSource<RpcResponse>();
        _pendingRpcRequests[requestId] = tcs;

        try {
            await SendMessageInternalAsync(rpcRequest);

            using var cts = new CancellationTokenSource(timeout);
            cts.Token.Register(() => {
                if (_pendingRpcRequests.TryRemove(requestId, out var removed)) {
                    removed.TrySetCanceled();
                }
            });

            return await tcs.Task;
        }
        catch (OperationCanceledException) {
            _logger.LogWarning("RPC request timed out: {RequestId}", requestId);
            throw new TimeoutException($"RPC request timed out: {requestId}");
        }
        finally {
            _pendingRpcRequests.TryRemove(requestId, out _);
        }
    }

    private async Task SendMessageInternalAsync(object message) {
        if (_port == null) {
            throw new InvalidOperationException("Port is not connected");
        }

        try {
            // Use the postMessageToPort helper defined in app.ts
            await _jsRuntime.InvokeVoidAsync("postMessageToPort", _port, message);
            _logger.LogDebug("Sent port message: {Type}", message.GetType().Name);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to send port message");
            throw;
        }
    }

    private async Task HandlePortMessageAsync(object messageObj) {
        try {
            var messageJson = JsonSerializer.Serialize(messageObj, JsonOptions);
            var baseMsg = JsonSerializer.Deserialize<JsonElement>(messageJson, JsonOptions);

            if (!baseMsg.TryGetProperty("t", out var typeElement)) {
                _logger.LogWarning("Port message missing 't' property");
                return;
            }

            var messageType = typeElement.GetString();
            _logger.LogDebug("Port message received: type={Type}", messageType);

            switch (messageType) {
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
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling port message");
        }
    }

    private void HandleReadyMessage(string messageJson) {
        try {
            var readyMessage = JsonSerializer.Deserialize<ReadyMessage>(messageJson, JsonOptions);
            if (readyMessage == null) {
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
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling READY message");
            _connectTcs?.TrySetException(ex);
        }
    }

    private void HandleRpcResponse(string messageJson) {
        try {
            var rpcResponse = JsonSerializer.Deserialize<RpcResponse>(messageJson, JsonOptions);
            if (rpcResponse == null) {
                _logger.LogError("Failed to deserialize RPC_RES message");
                return;
            }

            _logger.LogDebug("RPC_RES received: id={Id}, error={Error}",
                rpcResponse.Id, rpcResponse.Error);

            // Complete the pending request
            if (_pendingRpcRequests.TryRemove(rpcResponse.Id, out var tcs)) {
                tcs.TrySetResult(rpcResponse);
            } else {
                _logger.LogWarning("No pending request found for id={Id}", rpcResponse.Id);
            }

            // Raise event
            MessageReceived?.Invoke(this, rpcResponse);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling RPC_RES message");
        }
    }

    private void HandleEventMessage(string messageJson) {
        try {
            var eventMessage = JsonSerializer.Deserialize<EventMessage>(messageJson, JsonOptions);
            if (eventMessage == null) {
                _logger.LogError("Failed to deserialize EVENT message");
                return;
            }

            _logger.LogDebug("EVENT received: name={Name}", eventMessage.Name);

            // Raise event
            MessageReceived?.Invoke(this, eventMessage);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling EVENT message");
        }
    }

    private void HandleErrorMessage(string messageJson) {
        try {
            var errorMessage = JsonSerializer.Deserialize<ErrorMessage>(messageJson, JsonOptions);
            if (errorMessage == null) {
                _logger.LogError("Failed to deserialize ERROR message");
                return;
            }

            _logger.LogWarning("ERROR received: code={Code}, message={Message}",
                errorMessage.Code, errorMessage.Message);

            // Raise event
            MessageReceived?.Invoke(this, errorMessage);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling ERROR message");
        }
    }

    private void HandleDisconnect() {
        _logger.LogInformation("Port disconnected");

        IsConnected = false;
        PortSessionId = null;
        OriginTabId = null;
        AttachedTabId = null;
        _port = null;

        // Cancel any pending requests
        foreach (var kvp in _pendingRpcRequests) {
            kvp.Value.TrySetCanceled();
        }
        _pendingRpcRequests.Clear();

        // Cancel connect if in progress
        _connectTcs?.TrySetCanceled();

        // Raise event
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void DisconnectInternal() {
        if (_port != null) {
            try {
                _port.Disconnect();
            }
            catch (Exception ex) {
                _logger.LogDebug(ex, "Error during port disconnect");
            }
            _port = null;
        }

        IsConnected = false;
        PortSessionId = null;
        OriginTabId = null;
        AttachedTabId = null;
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) {
            return;
        }
        _disposed = true;

        DisconnectInternal();
        await Task.CompletedTask; // Satisfy async requirement
        GC.SuppressFinalize(this);
    }
}
