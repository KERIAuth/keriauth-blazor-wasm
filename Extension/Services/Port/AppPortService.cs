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
public class AppPortService(
    ILogger<AppPortService> logger,
    IWebExtensionsApi webExtensionsApi,
    IJSRuntime jsRuntime) : IAppPortService
{
    private readonly ILogger<AppPortService> _logger = logger;
    private readonly IJSRuntime _jsRuntime = jsRuntime;
    private WebExtensions.Net.Runtime.Port? _port;
    private readonly string _instanceId = Guid.NewGuid().ToString();
    private TaskCompletionSource<ReadyMessage>? _connectTcs;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RpcResponse>> _pendingRpcRequests = new();
    private readonly List<IObserver<BwAppMessage>> _observers = [];
    private bool _disposed;

    // Heartbeat watchdog
    private DateTime _lastHeartbeatUtc = DateTime.MinValue;
    private Timer? _heartbeatWatchdog;
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(AppConfig.HeartbeatTimeoutSeconds);
    private static readonly TimeSpan HeartbeatWatchdogInterval = TimeSpan.FromSeconds(10);

    // Default timeout for RPC requests
    private static readonly TimeSpan DefaultRpcTimeout = TimeSpan.FromSeconds(30);

    // ConnectAsync timeout settings
    // Phase 1 polls via sendMessage until BW responds with ready=true.
    // Each poll is a quick synchronous round-trip (no deferred sendResponse).
    // The BW may be loading modules or initializing WASM; polls return ready=false until
    // Program.cs signals __keriauth_setBwReady. WASM cold start from inactive SW can take
    // 60-90s (29 modules + WASM compilation), so the timeout must be generous.
    private static readonly TimeSpan WasmReadyTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    // Phase 2: port HELLO/READY handshake (fast once BW is confirmed ready).
    private static readonly TimeSpan PortHandshakeTimeout = TimeSpan.FromSeconds(5);
    private const int MaxPortAttempts = 3;
    private static readonly TimeSpan PortRetryDelay = TimeSpan.FromSeconds(1);

    public bool IsConnected => !(_port is null || PortSessionId is null);
    public string? PortSessionId { get; private set; }
    public int? OriginTabId { get; private set; }
    public int? AttachedTabId { get; private set; }

    public event EventHandler<PortMessage>? MessageReceived;
    public event EventHandler? Disconnected;

    public Task StartAsync()
    {
        // No-op: SW_CLIENT_HELLO is now received as the sendResponse to CLIENT_SW_HELLO
        // (same message channel). No separate runtime.onMessage listener needed.
        _logger.LogInformation("AppPortService.StartAsync called (no listener registration needed)");
        return Task.CompletedTask;
    }

    public async Task ConnectAsync()
    {
        if (IsConnected)
        {
            _logger.LogDebug("ConnectAsync called but already connected");
            return;
        }

        // Phase 1: Fire CLIENT_SW_HELLO to wake the SW, then poll for the
        // async SW_CLIENT_HELLO reply (received by app.ts onMessage listener).
        _logger.LogInformation("ConnectAsync Phase 1: Polling for BW readiness (timeout={Timeout}s, interval={Interval}s)...",
            WasmReadyTimeout.TotalSeconds, PollInterval.TotalSeconds);

        var deadline = DateTime.UtcNow + WasmReadyTimeout;
        var pollAttempt = 0;
        var bwReady = false;

        while (DateTime.UtcNow < deadline)
        {
            pollAttempt++;
            try
            {
                // Fire-and-forget: wakes BW if inactive. app.ts [BW] onMessage
                // handler replies with a separate SW_CLIENT_HELLO message.
                _logger.LogInformation("ConnectAsync Phase 1: Sending CLIENT_SW_HELLO (attempt {Attempt})", pollAttempt);
                await _jsRuntime.InvokeVoidAsync("chrome.runtime.sendMessage",
                    new { t = SendMessageTypes.ClientHello });

                // Check if app.ts listener has received SW_CLIENT_HELLO
                bwReady = await _jsRuntime.InvokeAsync<bool>("__keriauth_isBwReady");
                if (bwReady)
                {
                    _logger.LogInformation("ConnectAsync Phase 1: BW ready (attempt {Attempt})", pollAttempt);
                    break;
                }

                if (pollAttempt % 5 == 0)
                {
                    _logger.LogInformation("ConnectAsync Phase 1: Waiting for BW... (attempt {Attempt})", pollAttempt);
                }
                else
                {
                    _logger.LogDebug("ConnectAsync Phase 1: Not ready (attempt {Attempt})", pollAttempt);
                }
            }
            catch (JSException ex)
            {
                _logger.LogWarning("ConnectAsync Phase 1: Probe {Attempt} JS error: {Error}", pollAttempt, ex.Message);
            }

            if (DateTime.UtcNow + PollInterval > deadline)
            {
                break;
            }

            await Task.Delay(PollInterval);
        }

        if (!bwReady)
        {
            _logger.LogError("ConnectAsync Phase 1: BW did not become ready within {Timeout}s ({Attempts} attempts)",
                WasmReadyTimeout.TotalSeconds, pollAttempt);
            throw new TimeoutException(
                $"BackgroundWorker did not become ready within {WasmReadyTimeout.TotalSeconds}s");
        }

        // =====================================================================
        // Phase 2: Create port and complete HELLO/READY handshake.
        // WASM is confirmed alive, so this should be fast. Retry on timeout
        // since transient issues (port creation race) are possible.
        // =====================================================================
        for (int attempt = 1; attempt <= MaxPortAttempts; attempt++)
        {
            _logger.LogInformation("ConnectAsync Phase 2: Port handshake attempt {Attempt}/{Max}...",
                attempt, MaxPortAttempts);

            try
            {
                _connectTcs = new TaskCompletionSource<ReadyMessage>();
                var connectInfo = new WebExtensions.Net.Runtime.ConnectInfo { Name = "extension-app" };
                _port = webExtensionsApi.Runtime.Connect(connectInfo: connectInfo);

                // Set up message listener
                Func<object, WebExtensions.Net.Runtime.MessageSender, Action<object>, bool> onMessageHandler =
                    (object message, WebExtensions.Net.Runtime.MessageSender sender, Action<object> sendResponse) =>
                    {
                        _ = HandlePortMessageAsync(message);
                        return false;
                    };
                _port.OnMessage.AddListener(onMessageHandler);

                // Set up disconnect listener (still used as early signal)
                Action<WebExtensions.Net.Runtime.Port> onDisconnectHandler = (WebExtensions.Net.Runtime.Port disconnectedPort) =>
                {
                    HandleDisconnect();
                };
                _port.OnDisconnect.AddListener(onDisconnectHandler);

                // Send HELLO on port for session establishment
                var helloMessage = new HelloMessage
                {
                    Context = ContextKind.ExtensionApp,
                    InstanceId = _instanceId,
                    TabId = null,
                    FrameId = null
                };

                await SendMessageInternalAsync(helloMessage);
                _logger.LogDebug("HELLO sent, waiting for READY (attempt {Attempt})...", attempt);

                // Wait for port READY
                using var portCts = new CancellationTokenSource(PortHandshakeTimeout);
                portCts.Token.Register(() => _connectTcs?.TrySetCanceled());

                var readyMessage = await _connectTcs.Task;

                PortSessionId = readyMessage.PortSessionId;
                OriginTabId = readyMessage.TabId;

                // Start heartbeat watchdog now that we're connected
                StartHeartbeatWatchdog();

                _logger.LogInformation("Connected to BackgroundWorker, portSessionId={PortSessionId}, originTabId={OriginTabId}",
                    PortSessionId, OriginTabId);
                return; // Success
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("ConnectAsync Phase 2: Port handshake attempt {Attempt} timed out", attempt);
                ClearPort();

                if (attempt < MaxPortAttempts)
                {
                    _logger.LogInformation("Retrying port handshake in {Delay}...", PortRetryDelay);
                    await Task.Delay(PortRetryDelay);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ConnectAsync Phase 2: Port handshake attempt {Attempt} failed", attempt);
                ClearPort();
                throw;
            }
            finally
            {
                _connectTcs = null;
            }
        }

        throw new TimeoutException(
            $"Failed to complete port handshake after {MaxPortAttempts} attempts (WASM was alive)");
    }

    public async Task AttachToTabAsync(int tabId, int? frameId = null)
    {
        if (!IsConnected)
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
        if (!IsConnected)
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
        if (!IsConnected)
        {
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        }

        await SendMessageInternalAsync(message);
    }

    public async Task<RpcResponse> SendRpcRequestAsync(string method, object? parameters = null, TimeSpan? timeout = null)
    {
        if (!IsConnected)
        {
            // If not connected (which is expected to happen if the service-worker became inactive, for instance), attempt to connect before sending the RPC request
            await ConnectAsync();
        }

        if (string.IsNullOrEmpty(PortSessionId))
        {
            throw new InvalidOperationException("PortSessionId is null or empty after connection");
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
        if (!IsConnected)
        {
            // Reconnect if needed (e.g., service worker restarted)
            await ConnectAsync();
        }

        if (string.IsNullOrEmpty(PortSessionId))
        {
            throw new InvalidOperationException("PortSessionId is null or empty after connection");
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
                case PortMessageTypes.Ready:
                    HandleReadyMessage(messageJson);
                    break;
                case PortMessageTypes.RpcResponse:
                    HandleRpcResponse(messageJson);
                    break;
                case PortMessageTypes.Event:
                    HandleEventMessage(messageJson);
                    break;
                case PortMessageTypes.Error:
                    HandleErrorMessage(messageJson);
                    break;
                case PortMessageTypes.Heartbeat:
                    _lastHeartbeatUtc = DateTime.UtcNow;
                    _logger.LogDebug("Heartbeat received");
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
        _logger.LogInformation("Port disconnected from BackgroundWorker. IsConnected was {WasConnected}, PortSessionId was {PortSessionId}",
            IsConnected, PortSessionId);

        StopHeartbeatWatchdog();
        ClearPort();

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
    /// Clean up port state on disconnect or dispose.
    /// </summary>
    private void ClearPort()
    {
        _port = null;
        PortSessionId = null;
        OriginTabId = null;
        AttachedTabId = null;
    }

    public async Task CheckForWakeSignalAsync()
    {
        try
        {
            var wake = await _jsRuntime.InvokeAsync<bool>("__keriauth_checkAppWake");
            if (wake && !IsConnected)
            {
                _logger.LogInformation("CheckForWakeSignalAsync: SW_APP_WAKE received, reconnecting...");
                await ConnectAsync();
            }
        }
        catch (JSException)
        {
            // Flag not registered yet (beforeStart hasn't run)
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        StopHeartbeatWatchdog();

        // Notify observers of completion
        foreach (var observer in _observers.ToArray())
        {
            observer.OnCompleted();
        }
        _observers.Clear();

        ClearPort();
        await Task.CompletedTask; // Satisfy async requirement
        GC.SuppressFinalize(this);
    }

    #region Heartbeat Watchdog

    private void StartHeartbeatWatchdog()
    {
        StopHeartbeatWatchdog();
        _lastHeartbeatUtc = DateTime.UtcNow; // Initialize so watchdog doesn't fire immediately
        _heartbeatWatchdog = new Timer(CheckHeartbeat, null, HeartbeatWatchdogInterval, HeartbeatWatchdogInterval);
        _logger.LogDebug("Heartbeat watchdog started");
    }

    private void StopHeartbeatWatchdog()
    {
        _heartbeatWatchdog?.Dispose();
        _heartbeatWatchdog = null;
    }

    private void CheckHeartbeat(object? state)
    {
        if (_lastHeartbeatUtc == DateTime.MinValue) return; // No heartbeat received yet
        if (!IsConnected) return;

        if (DateTime.UtcNow - _lastHeartbeatUtc > HeartbeatTimeout)
        {
            _logger.LogWarning("Heartbeat timeout — no heartbeat for {Seconds}s", HeartbeatTimeout.TotalSeconds);
            HandleDisconnect();
        }
    }

    #endregion

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
        return new Unsubscriber(this, observer);
    }

    /// <summary>
    /// Notify all observers of a new BwAppMessage.
    /// Snapshots the list with .ToArray() so that unsubscribe during OnNext is safe.
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
    /// Removes an observer from the subscription list.
    /// Called by Unsubscriber.Dispose to avoid leaking the raw _observers list.
    /// </summary>
    private void RemoveObserver(IObserver<BwAppMessage> observer)
    {
        _observers.Remove(observer);
    }

    /// <summary>
    /// Helper class for managing observer unsubscription.
    /// Uses a service reference rather than a direct list reference to encapsulate the _observers list.
    /// </summary>
    private sealed class Unsubscriber(AppPortService service, IObserver<BwAppMessage> observer) : IDisposable
    {
        public void Dispose()
        {
            if (observer != null)
            {
                service.RemoveObserver(observer);
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

        if (!IsConnected)
        {
            await ConnectAsync();
            //throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
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
