using System.Collections.Concurrent;
using System.Text.Json;
using Extension.Helper;
using Extension.Models;
using Extension.Models.Messages.Port;
using Microsoft.JSInterop;
using WebExtensions.Net;

namespace Extension.Services.Port;

/// <summary>
/// Service for managing port connections in the BackgroundWorker context.
/// Handles ContentScript and App port connections, PortSession lifecycle, and message routing.
/// </summary>
public class BwPortService : IBwPortService
{
    private readonly ILogger<BwPortService> _logger;
    private readonly IJSRuntime _jsRuntime;
    private readonly IWebExtensionsApi _webExtensionsApi;
    private readonly IPendingBwAppRequestService _pendingRequestService;

    // Port registry - use port IDs (strings) as keys, not Port objects
    // See tmpMessagingArch.md Section 5 for architecture details
    private readonly ConcurrentDictionary<string, PortSession> _portSessionsByTabKey = new();
    private readonly ConcurrentDictionary<Guid, PortSession> _portSessionsById = new();
    private readonly ConcurrentDictionary<string, PortSession> _portIdToPortSession = new();
    private readonly ConcurrentDictionary<string, WebExtensions.Net.Runtime.Port> _portsById = new();
    private readonly ConcurrentDictionary<string, string> _appInstanceIdToPortId = new();

    // Pending popup context - tab ID from OnActionClicked
    private int? _pendingPopupTabId;

    // RPC request handlers - registered by BackgroundWorker
    private RpcRequestHandler? _contentScriptRpcHandler;
    private RpcRequestHandler? _appRpcHandler;

    // Event handler - registered by BackgroundWorker
    private PortEventMessageHandler? _eventHandler;

    // Track which ports are ContentScript vs App (by portId)
    private readonly ConcurrentDictionary<string, bool> _portIsContentScript = new();

    public BwPortService(
        ILogger<BwPortService> logger,
        IJSRuntime jsRuntime,
        IWebExtensionsApi webExtensionsApi,
        IPendingBwAppRequestService pendingRequestService)
    {
        _logger = logger;
        _jsRuntime = jsRuntime;
        _webExtensionsApi = webExtensionsApi;
        _pendingRequestService = pendingRequestService;
    }

    public int ActivePortSessionCount => _portSessionsById.Count;
    public int ActivePortCount => _portsById.Count;

    public async Task HandleConnectAsync(WebExtensions.Net.Runtime.Port port)
    {
        var portId = Guid.NewGuid().ToString();
        _portsById[portId] = port;

        _logger.LogInformation("Port connected: name={Name}, portId={PortId}", port.Name, portId);

        // Set up message listener with explicit delegate type to resolve ambiguity
        // WebExtensions.Net OnMessage expects: Func<object, MessageSender, Action<object>, bool>
        Func<object, WebExtensions.Net.Runtime.MessageSender, Action<object>, bool> onMessageHandler =
            (object message, WebExtensions.Net.Runtime.MessageSender sender, Action<object> sendResponse) =>
            {
                _ = HandlePortMessageAsync(portId, message);
                return false; // Don't keep channel open
            };
        port.OnMessage.AddListener(onMessageHandler);

        // Set up disconnect listener with explicit delegate type
        // WebExtensions.Net OnDisconnect expects: Action<Port>
        Action<WebExtensions.Net.Runtime.Port> onDisconnectHandler = (WebExtensions.Net.Runtime.Port disconnectedPort) =>
        {
            _ = HandleDisconnectAsync(portId);
        };

        port.OnDisconnect.AddListener(onDisconnectHandler);

        await Task.CompletedTask; // Satisfy async method requirement
    }

    private async Task HandlePortMessageAsync(string portId, object messageObj)
    {
        try
        {
            var messageJson = JsonSerializer.Serialize(messageObj, JsonOptions.PortMessaging);
            var baseMsg = JsonSerializer.Deserialize<JsonElement>(messageJson, JsonOptions.PortMessaging);

            if (!baseMsg.TryGetProperty("t", out var typeElement))
            {
                _logger.LogWarning("Port message missing 't' property from portId={PortId}", portId);
                return;
            }

            var messageType = typeElement.GetString();
            _logger.LogDebug("Port message received: type={Type}, portId={PortId}", messageType, portId);

            switch (messageType)
            {
                case PortMessageTypes.Hello:
                    await HandleHelloAsync(portId, messageJson);
                    break;
                case PortMessageTypes.AttachTab:
                    await HandleAttachTabAsync(portId, messageJson);
                    break;
                case PortMessageTypes.DetachTab:
                    HandleDetachTab(portId);
                    break;
                case PortMessageTypes.RpcRequest:
                case CsBwPortMessageTypes.RpcRequest:  // Directional variant for log clarity
                    await HandleRpcRequestAsync(portId, messageJson);
                    break;
                case PortMessageTypes.Event:
                    await HandleEventAsync(portId, messageJson);
                    break;
                default:
                    _logger.LogWarning("Unknown port message type: {Type} from portId={PortId}", messageType, portId);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling port message from portId={PortId}", portId);
        }
    }

    private async Task HandleHelloAsync(string portId, string messageJson)
    {
        var hello = JsonSerializer.Deserialize<HelloMessage>(messageJson, JsonOptions.PortMessaging);
        if (hello is null)
        {
            _logger.LogWarning("Failed to deserialize HELLO message from portId={PortId}", portId);
            return;
        }

        if (!_portsById.TryGetValue(portId, out var port))
        {
            _logger.LogWarning("HELLO from unknown portId={PortId}", portId);
            return;
        }

        _logger.LogInformation("HELLO received: context={Context}, instanceId={InstanceId}, portId={PortId}",
            hello.Context, hello.InstanceId, portId);

        PortSession portSession;

        if (hello.Context == ContextKind.ContentScript)
        {
            // Track this port as ContentScript
            _portIsContentScript[portId] = true;

            // ContentScript connects: create or get PortSession for tab
            var tabId = port.Sender?.Tab?.Id;
            var frameId = port.Sender?.FrameId;

            if (!tabId.HasValue)
            {
                _logger.LogWarning("HELLO from ContentScript without tabId, portId={PortId}", portId);
                return;
            }

            var tabKey = $"{tabId}:{frameId ?? 0}";
            portSession = _portSessionsByTabKey.GetOrAdd(tabKey, _ =>
            {
                var newSession = new PortSession
                {
                    PortSessionId = Guid.NewGuid(),
                    TabId = tabId,
                    FrameId = frameId,
                    TabUrl = port.Sender?.Url
                };
                _portSessionsById[newSession.PortSessionId] = newSession;
                return newSession;
            });

            portSession.ContentScriptPortId = portId;
            portSession.TabUrl = port.Sender?.Url; // Update URL in case it changed
            _portIdToPortSession[portId] = portSession;

            _logger.LogInformation("ContentScript PortSession established: portSessionId={PortSessionId}, tabKey={TabKey}",
                portSession.PortSessionId, tabKey);

            // Send READY with portSessionId and tab info
            var ready = new ReadyMessage
            {
                PortSessionId = portSession.PortSessionId.ToString(),
                TabId = tabId,
                FrameId = frameId
            };
            await SendToPortAsync(portId, ready);
        }
        else
        {
            // Track this port as App (not ContentScript)
            _portIsContentScript[portId] = false;

            // App connects: create non-tab-scoped PortSession
            portSession = new PortSession
            {
                PortSessionId = Guid.NewGuid()
            };
            _portSessionsById[portSession.PortSessionId] = portSession;
            _appInstanceIdToPortId[hello.InstanceId] = portId;
            _portIdToPortSession[portId] = portSession;

            // Get pending popup context (tab that was clicked when action was invoked)
            var originTabId = _pendingPopupTabId;
            _pendingPopupTabId = null; // Clear after use

            _logger.LogInformation("App PortSession established: portSessionId={PortSessionId}, originTabId={OriginTabId}",
                portSession.PortSessionId, originTabId);

            var ready = new ReadyMessage
            {
                PortSessionId = portSession.PortSessionId.ToString(),
                TabId = originTabId
            };
            await SendToPortAsync(portId, ready);
        }
    }

    private async Task HandleAttachTabAsync(string portId, string messageJson)
    {
        var attach = JsonSerializer.Deserialize<AttachTabMessage>(messageJson, JsonOptions.PortMessaging);
        if (attach is null)
        {
            _logger.LogWarning("Failed to deserialize ATTACH_TAB message from portId={PortId}", portId);
            return;
        }

        var tabKey = $"{attach.TabId}:{attach.FrameId ?? 0}";

        if (!_portSessionsByTabKey.TryGetValue(tabKey, out var portSession))
        {
            _logger.LogWarning("ATTACH_TAB failed: no PortSession for tabKey={TabKey}, portId={PortId}", tabKey, portId);

            // Send error response
            var error = new ErrorMessage
            {
                Code = PortErrorCodes.AttachFailed,
                Message = $"No PortSession exists for tab {attach.TabId}. Content script may not be injected."
            };
            await SendToPortAsync(portId, error);
            return;
        }

        // Detach from any previous PortSession
        if (_portIdToPortSession.TryGetValue(portId, out var previousSession))
        {
            previousSession.AttachedAppPortIds.Remove(portId);
            _logger.LogDebug("Detached portId={PortId} from previous portSessionId={PortSessionId}",
                portId, previousSession.PortSessionId);
        }

        // Attach to the new PortSession
        if (!portSession.AttachedAppPortIds.Contains(portId))
        {
            portSession.AttachedAppPortIds.Add(portId);
        }
        _portIdToPortSession[portId] = portSession;

        _logger.LogInformation("App attached to tab {TabId}: portId={PortId}, portSessionId={PortSessionId}",
            attach.TabId, portId, portSession.PortSessionId);
    }

    private void HandleDetachTab(string portId)
    {
        if (!_portIdToPortSession.TryGetValue(portId, out var portSession))
        {
            _logger.LogDebug("DETACH_TAB: portId={PortId} not attached to any PortSession", portId);
            return;
        }

        portSession.AttachedAppPortIds.Remove(portId);
        _logger.LogInformation("App detached from portSessionId={PortSessionId}, portId={PortId}",
            portSession.PortSessionId, portId);
    }

    private async Task HandleRpcRequestAsync(string portId, string messageJson)
    {
        var rpcRequest = JsonSerializer.Deserialize<RpcRequest>(messageJson, JsonOptions.PortMessaging);
        if (rpcRequest is null)
        {
            _logger.LogWarning("Failed to deserialize RPC_REQ message from portId={PortId}", portId);
            return;
        }

        _logger.LogInformation("RPC_REQ received: method={Method}, id={Id}, portId={PortId}",
            rpcRequest.Method, rpcRequest.Id, portId);

        // Get the PortSession for context
        _portIdToPortSession.TryGetValue(portId, out var portSession);

        // Determine if this is from ContentScript or App
        var isFromContentScript = _portIsContentScript.TryGetValue(portId, out var isCs) && isCs;

        // Select the appropriate handler
        var handler = isFromContentScript ? _contentScriptRpcHandler : _appRpcHandler;

        if (handler is null)
        {
            _logger.LogWarning("No RPC handler registered for {Source}, portId={PortId}",
                isFromContentScript ? "ContentScript" : "App", portId);
            var errorResponse = new RpcResponse
            {
                Discriminator = isFromContentScript ? CsBwPortMessageTypes.RpcResponse : PortMessageTypes.RpcResponse,
                PortSessionId = rpcRequest.PortSessionId,
                Id = rpcRequest.Id,
                Ok = false,
                Error = "No handler registered for this request type"
            };
            await SendToPortAsync(portId, errorResponse);
            return;
        }

        try
        {
            // Call the registered handler - it's responsible for sending the response
            await handler(portId, portSession, rpcRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in RPC handler for method={Method}, portId={PortId}",
                rpcRequest.Method, portId);

            // Send error response if handler threw
            var errorResponse = new RpcResponse
            {
                Discriminator = isFromContentScript ? CsBwPortMessageTypes.RpcResponse : PortMessageTypes.RpcResponse,
                PortSessionId = rpcRequest.PortSessionId,
                Id = rpcRequest.Id,
                Ok = false,
                Error = $"Handler error: {ex.Message}"
            };
            await SendToPortAsync(portId, errorResponse);
        }
    }

    private async Task HandleEventAsync(string portId, string messageJson)
    {
        var eventMsg = JsonSerializer.Deserialize<EventMessage>(messageJson, JsonOptions.PortMessaging);
        if (eventMsg is null)
        {
            _logger.LogWarning("Failed to deserialize EVENT message from portId={PortId}", portId);
            return;
        }

        _logger.LogDebug("EVENT received: name={Name}, portId={PortId}", eventMsg.Name, portId);

        // Get port session for the event
        _portIdToPortSession.TryGetValue(portId, out var portSession);

        // Route to registered handler
        if (_eventHandler is not null)
        {
            try
            {
                await _eventHandler(portId, portSession, eventMsg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling event: name={Name}", eventMsg.Name);
            }
        }
        else
        {
            _logger.LogWarning("No event handler registered for event: name={Name}", eventMsg.Name);
        }
    }

    private async Task HandleDisconnectAsync(string portId)
    {
        _logger.LogInformation("Port disconnected: portId={PortId}", portId);

        // Remove from ports registry
        _portsById.TryRemove(portId, out _);

        // Find and update the associated PortSession
        if (_portIdToPortSession.TryRemove(portId, out var portSession))
        {
            if (portSession.ContentScriptPortId == portId)
            {
                // ContentScript disconnected
                portSession.ContentScriptPortId = null;
                _logger.LogInformation("ContentScript disconnected from portSessionId={PortSessionId}",
                    portSession.PortSessionId);

                // If no App attached, destroy the PortSession
                if (!portSession.HasAttachedApps)
                {
                    CleanupPortSession(portSession);
                }
            }
            else
            {
                // App disconnected - clean up any orphaned pending requests
                await CleanupOrphanedRequestsAsync(portSession);

                portSession.AttachedAppPortIds.Remove(portId);
                _logger.LogInformation("App disconnected from portSessionId={PortSessionId}",
                    portSession.PortSessionId);
            }
        }

        // Remove from app instance tracking
        var instanceToRemove = _appInstanceIdToPortId.FirstOrDefault(kvp => kvp.Value == portId).Key;
        if (instanceToRemove != null)
        {
            _appInstanceIdToPortId.TryRemove(instanceToRemove, out _);
        }
    }

    private void CleanupPortSession(PortSession portSession)
    {
        _logger.LogInformation("Cleaning up PortSession: portSessionId={PortSessionId}", portSession.PortSessionId);

        _portSessionsById.TryRemove(portSession.PortSessionId, out _);

        if (portSession.TabId.HasValue)
        {
            var tabKey = portSession.GetTabKey();
            _portSessionsByTabKey.TryRemove(tabKey, out _);
        }

        // Disconnect any remaining ports
        if (!string.IsNullOrEmpty(portSession.ContentScriptPortId))
        {
            DisconnectPort(portSession.ContentScriptPortId);
        }

        foreach (var appPortId in portSession.AttachedAppPortIds.ToList())
        {
            DisconnectPort(appPortId);
        }
    }

    private void DisconnectPort(string portId)
    {
        if (_portsById.TryRemove(portId, out var port))
        {
            try
            {
                port.Disconnect();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disconnecting port: portId={PortId}", portId);
            }
        }
        _portIdToPortSession.TryRemove(portId, out _);
    }

    /// <summary>
    /// Cleans up orphaned pending requests when an App disconnects without sending a reply.
    /// Sends cancel RPC responses to the ContentScript for any pending requests.
    /// </summary>
    /// <remarks>
    /// Internal visibility for unit testing.
    /// </remarks>
    internal async Task CleanupOrphanedRequestsAsync(PortSession portSession)
    {
        _logger.LogDebug(
            "CleanupOrphanedRequestsAsync: Checking for orphaned requests, portSessionId={PortSessionId}, tabId={TabId}",
            portSession.PortSessionId, portSession.TabId);

        try
        {
            var pendingResult = await _pendingRequestService.GetRequestsAsync();
            if (pendingResult.IsFailed)
            {
                _logger.LogWarning("CleanupOrphanedRequestsAsync: Failed to get pending requests: {Errors}",
                    string.Join(", ", pendingResult.Errors));
                return;
            }

            if (pendingResult.Value.IsEmpty)
            {
                _logger.LogDebug("CleanupOrphanedRequestsAsync: No pending requests found");
                return;
            }

            var pendingRequests = pendingResult.Value.Requests;
            _logger.LogDebug(
                "CleanupOrphanedRequestsAsync: Found {Count} pending request(s) to check",
                pendingRequests.Count);

            // Find requests for this port session that need cleanup
            foreach (var request in pendingRequests)
            {
                // The pending request stores the ContentScript's PortSessionId, but when App disconnects,
                // we have the App's PortSession which is different. We need to match by:
                // 1. TabId - if both the App's PortSession and the request have a TabId, match by that
                // 2. If App's PortSession has no TabId (popup that didn't attach), treat all pending requests
                //    as potentially orphaned since there should only be one pending request at a time
                bool isOrphaned;
                if (portSession.TabId.HasValue && request.TabId.HasValue)
                {
                    // Both have TabId - match by TabId
                    isOrphaned = request.TabId.Value == portSession.TabId.Value;
                }
                else if (!portSession.TabId.HasValue)
                {
                    // App's PortSession has no TabId (popup that didn't attach to a tab).
                    // Since only one pending request exists at a time, treat it as orphaned.
                    // TODO P2: If multiple Apps can be connected simultaneously (e.g., sidepanel + popup),
                    // this could incorrectly cancel a request being handled by another App. Consider tracking
                    // which pending request is actively being handled by which App instance.
                    isOrphaned = true;
                }
                else
                {
                    // Request has no TabId - shouldn't happen for CS-originated requests, but skip
                    isOrphaned = false;
                }

                _logger.LogDebug(
                    "CleanupOrphanedRequestsAsync: Checking request requestId={RequestId}, " +
                    "request.TabId={ReqTabId}, portSession.TabId={TabId}, isOrphaned={IsOrphaned}",
                    request.RequestId, request.TabId, portSession.TabId, isOrphaned);

                if (!isOrphaned)
                {
                    continue;
                }

                _logger.LogInformation(
                    "CleanupOrphanedRequestsAsync: Found orphaned request requestId={RequestId}, type={Type}, sending cancel to ContentScript",
                    request.RequestId, request.Type);

                // Send cancel RPC response to ContentScript if we have the port info
                if (!string.IsNullOrEmpty(request.PortId) &&
                    !string.IsNullOrEmpty(request.PortSessionId) &&
                    !string.IsNullOrEmpty(request.RpcRequestId))
                {
                    try
                    {
                        await SendRpcResponseAsync(
                            request.PortId,
                            request.PortSessionId,
                            request.RpcRequestId,
                            result: null,
                            errorMessage: "Dialog closed without response");
                    }
                    catch (Exception ex)
                    {
                        // Port may already be disconnected, log and continue
                        _logger.LogWarning(ex,
                            "CleanupOrphanedRequestsAsync: Failed to send cancel response for requestId={RequestId}",
                            request.RequestId);
                    }
                }

                // Remove the orphaned request from storage
                var removeResult = await _pendingRequestService.RemoveRequestAsync(request.RequestId);
                if (removeResult.IsFailed)
                {
                    _logger.LogWarning(
                        "CleanupOrphanedRequestsAsync: Failed to remove request requestId={RequestId}: {Errors}",
                        request.RequestId, string.Join(", ", removeResult.Errors));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CleanupOrphanedRequestsAsync: Unexpected error cleaning up orphaned requests");
        }
    }

    /// <inheritdoc/>
    public async Task CleanupAllPendingRequestsAsync(string errorMessage)
    {
        _logger.LogInformation("CleanupAllPendingRequestsAsync: Cleaning up all pending requests with error: {Error}", errorMessage);

        try
        {
            var pendingResult = await _pendingRequestService.GetRequestsAsync();
            if (pendingResult.IsFailed)
            {
                _logger.LogWarning("CleanupAllPendingRequestsAsync: Failed to get pending requests: {Errors}",
                    string.Join(", ", pendingResult.Errors));
                return;
            }

            if (pendingResult.Value.IsEmpty)
            {
                _logger.LogDebug("CleanupAllPendingRequestsAsync: No pending requests to clean up");
                return;
            }

            var pendingRequests = pendingResult.Value.Requests;
            _logger.LogInformation("CleanupAllPendingRequestsAsync: Found {Count} pending request(s) to clean up", pendingRequests.Count);

            foreach (var request in pendingRequests)
            {
                _logger.LogInformation(
                    "CleanupAllPendingRequestsAsync: Cleaning up request requestId={RequestId}, type={Type}",
                    request.RequestId, request.Type);

                // Send cancel RPC response to ContentScript if we have the port info
                if (!string.IsNullOrEmpty(request.PortId) &&
                    !string.IsNullOrEmpty(request.PortSessionId) &&
                    !string.IsNullOrEmpty(request.RpcRequestId))
                {
                    try
                    {
                        await SendRpcResponseAsync(
                            request.PortId,
                            request.PortSessionId,
                            request.RpcRequestId,
                            result: null,
                            errorMessage: errorMessage);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "CleanupAllPendingRequestsAsync: Failed to send cancel response for requestId={RequestId}",
                            request.RequestId);
                    }
                }

                // Remove the request from storage
                var removeResult = await _pendingRequestService.RemoveRequestAsync(request.RequestId);
                if (removeResult.IsFailed)
                {
                    _logger.LogWarning(
                        "CleanupAllPendingRequestsAsync: Failed to remove request requestId={RequestId}: {Errors}",
                        request.RequestId, string.Join(", ", removeResult.Errors));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CleanupAllPendingRequestsAsync: Unexpected error");
        }
    }

    public async Task HandleTabRemovedAsync(int tabId)
    {
        _logger.LogInformation("Tab removed: tabId={TabId}", tabId);

        // Find all PortSessions for this tab (across all frames)
        var sessionsToRemove = _portSessionsByTabKey
            .Where(kvp => kvp.Key.StartsWith($"{tabId}:", StringComparison.Ordinal))
            .Select(kvp => kvp.Value)
            .ToList();

        foreach (var portSession in sessionsToRemove)
        {
            CleanupPortSession(portSession);
        }
    }

    public async Task NotifyContentScriptsOfRestartAsync()
    {
        // Note: With port-based messaging, ContentScripts automatically reconnect when
        // their port disconnects due to SW restart. This method is primarily for backward
        // compatibility with any legacy sendMessage-based communication.
        //
        // At SW startup, the in-memory PortSession dictionaries are empty (state lost),
        // so we can't know which tabs had content scripts. Sending to all tabs would
        // cause "Receiving end does not exist" errors for tabs without content scripts.
        //
        // Instead, we rely on ContentScript's port disconnect handler to reconnect.
        // This method is now a no-op but kept for documentation and future use.

        _logger.LogDebug("NotifyContentScriptsOfRestartAsync: Port-based messaging handles reconnection automatically");
        await Task.CompletedTask;
    }

    public PortSession? GetPortSession(string portSessionId)
    {
        if (Guid.TryParse(portSessionId, out var guid) && _portSessionsById.TryGetValue(guid, out var session))
        {
            return session;
        }
        return null;
    }

    public PortSession? GetPortSessionByTab(int tabId, int frameId = 0)
    {
        var tabKey = $"{tabId}:{frameId}";
        _portSessionsByTabKey.TryGetValue(tabKey, out var session);
        return session;
    }

    public Task SendToPortAsync<T>(string portId, T message) where T : PortMessage
    {
        if (!_portsById.TryGetValue(portId, out var port))
        {
            _logger.LogWarning("SendToPortAsync: port not found for portId={PortId}", portId);
            return Task.CompletedTask;
        }

        try
        {
            // Pre-serialize the message using our JsonOptions to handle RecursiveDictionary
            // and deeply nested structures. Then deserialize to JsonElement which
            // WebExtensions.Net can serialize without custom converters.
            var serialized = JsonSerializer.Serialize(message, JsonOptions.PortMessaging);
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(serialized);

            port.PostMessage(jsonElement);
            _logger.LogDebug("Message sent to portId={PortId}, type={Type}", portId, message.T);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to portId={PortId}", portId);
        }
        return Task.CompletedTask;
    }

    public async Task SendToContentScriptAsync<T>(string portSessionId, T message) where T : PortMessage
    {
        var portSession = GetPortSession(portSessionId);
        if (portSession is null)
        {
            _logger.LogWarning("SendToContentScriptAsync: PortSession not found for portSessionId={PortSessionId}", portSessionId);
            return;
        }

        if (string.IsNullOrEmpty(portSession.ContentScriptPortId))
        {
            _logger.LogWarning("SendToContentScriptAsync: No ContentScript port for portSessionId={PortSessionId}", portSessionId);
            return;
        }

        await SendToPortAsync(portSession.ContentScriptPortId, message);
    }

    public async Task SendToAppsAsync<T>(string portSessionId, T message) where T : PortMessage
    {
        var portSession = GetPortSession(portSessionId);
        if (portSession is null)
        {
            _logger.LogWarning("SendToAppsAsync: PortSession not found for portSessionId={PortSessionId}", portSessionId);
            return;
        }

        foreach (var appPortId in portSession.AttachedAppPortIds)
        {
            await SendToPortAsync(appPortId, message);
        }
    }

    public void SetPendingPopupTabId(int tabId)
    {
        _pendingPopupTabId = tabId;
        _logger.LogDebug("Set pending popup tabId={TabId}", tabId);
    }

    public void RegisterContentScriptRpcHandler(RpcRequestHandler handler)
    {
        _contentScriptRpcHandler = handler;
        _logger.LogInformation("ContentScript RPC handler registered");
    }

    public void RegisterAppRpcHandler(RpcRequestHandler handler)
    {
        _appRpcHandler = handler;
        _logger.LogInformation("App RPC handler registered");
    }

    public void RegisterEventHandler(PortEventMessageHandler handler)
    {
        _eventHandler = handler;
        _logger.LogInformation("Event handler registered");
    }

    public async Task SendRpcResponseAsync(string portId, string portSessionId, string requestId, object? result = null, string? errorMessage = null)
    {
        // Use directional discriminator when responding to ContentScript
        var isFromContentScript = _portIsContentScript.TryGetValue(portId, out var isCs) && isCs;

        var response = new RpcResponse
        {
            Discriminator = isFromContentScript ? CsBwPortMessageTypes.RpcResponse : PortMessageTypes.RpcResponse,
            PortSessionId = portSessionId,
            Id = requestId,
            Ok = errorMessage is null,
            Result = result,
            Error = errorMessage
        };

        _logger.LogInformation("Sending RPC response: id={RequestId}, ok={Ok}, error={Error}",
            requestId, response.Ok, errorMessage ?? "null");

        await SendToPortAsync(portId, response);
    }

    public async Task SendEventToContentScriptAsync(string portSessionId, string eventName, object? data = null)
    {
        var portSession = GetPortSession(portSessionId);
        if (portSession is null)
        {
            _logger.LogWarning("SendEventToContentScriptAsync: PortSession not found for portSessionId={PortSessionId}", portSessionId);
            return;
        }

        if (string.IsNullOrEmpty(portSession.ContentScriptPortId))
        {
            _logger.LogWarning("SendEventToContentScriptAsync: No ContentScript port for portSessionId={PortSessionId}", portSessionId);
            return;
        }

        var eventMessage = new EventMessage
        {
            PortSessionId = portSessionId,
            Name = eventName,
            Data = data
        };

        await SendToPortAsync(portSession.ContentScriptPortId, eventMessage);
    }

    public async Task SendEventToAppsAsync(string portSessionId, string eventName, object? data = null)
    {
        var portSession = GetPortSession(portSessionId);
        if (portSession is null)
        {
            _logger.LogWarning("SendEventToAppsAsync: PortSession not found for portSessionId={PortSessionId}", portSessionId);
            return;
        }

        var eventMessage = new EventMessage
        {
            PortSessionId = portSessionId,
            Name = eventName,
            Data = data
        };

        foreach (var appPortId in portSession.AttachedAppPortIds)
        {
            await SendToPortAsync(appPortId, eventMessage);
        }
    }

    public PortSession? GetPortSessionByPortId(string portId)
    {
        _portIdToPortSession.TryGetValue(portId, out var session);
        return session;
    }

    public bool HasActivePortSessionForTab(int tabId, int frameId = 0)
    {
        var tabKey = $"{tabId}:{frameId}";
        if (_portSessionsByTabKey.TryGetValue(tabKey, out var session))
        {
            return session.HasContentScript;
        }
        return false;
    }

    public async Task BroadcastEventToAllAppsAsync(string eventName, object? data = null)
    {
        _logger.LogInformation("Broadcasting event to all apps: name={EventName}", eventName);

        // Collect all unique app port IDs across all port sessions
        var appPortIds = new HashSet<string>();
        foreach (var portSession in _portSessionsById.Values)
        {
            foreach (var appPortId in portSession.AttachedAppPortIds)
            {
                appPortIds.Add(appPortId);
            }
        }

        // Also include any app ports that may not be attached to a specific tab session
        // (e.g., extension options page, standalone tabs)
        foreach (var (portId, isContentScript) in _portIsContentScript)
        {
            if (!isContentScript)
            {
                appPortIds.Add(portId);
            }
        }

        _logger.LogDebug("Broadcasting to {Count} app ports", appPortIds.Count);

        foreach (var appPortId in appPortIds)
        {
            try
            {
                // Get the port session ID for this app (may be empty if not attached)
                _portIdToPortSession.TryGetValue(appPortId, out var portSession);
                var portSessionId = portSession?.PortSessionId.ToString() ?? "";

                var eventMessage = new EventMessage
                {
                    PortSessionId = portSessionId,
                    Name = eventName,
                    Data = data
                };

                await SendToPortAsync(appPortId, eventMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send event to app port: portId={PortId}", appPortId);
            }
        }
    }
}
