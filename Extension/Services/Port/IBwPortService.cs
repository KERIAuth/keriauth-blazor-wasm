using Extension.Models;
using Extension.Models.Messages.Port;
using WebExtensions.Net.Runtime;

namespace Extension.Services.Port;

/// <summary>
/// Delegate for handling RPC requests from ContentScript or App.
/// </summary>
/// <param name="portId">The port ID that sent the request.</param>
/// <param name="portSession">The PortSession associated with the request (may be null for App ports not attached to tabs).</param>
/// <param name="request">The RPC request.</param>
/// <returns>Task that completes when the request has been handled. The handler is responsible for sending the response via SendRpcResponseAsync.</returns>
public delegate Task RpcRequestHandler(string portId, PortSession? portSession, RpcRequest request);

/// <summary>
/// Delegate for handling EVENT messages from ContentScript or App.
/// Events are fire-and-forget notifications that don't require a response.
/// </summary>
/// <param name="portId">The port ID that sent the event.</param>
/// <param name="portSession">The PortSession associated with the event (may be null for App ports not attached to tabs).</param>
/// <param name="eventMessage">The event message.</param>
/// <returns>Task that completes when the event has been handled.</returns>
public delegate Task PortEventMessageHandler(string portId, PortSession? portSession, EventMessage eventMessage);

/// <summary>
/// Service interface for managing port connections in the BackgroundWorker context.
/// Handles ContentScript and App port connections, PortSession lifecycle, and message routing.
/// </summary>
public interface IBwPortService {
    /// <summary>
    /// Handles a new port connection from ContentScript or App.
    /// Called from BackgroundWorker.OnConnectAsync when a port connects.
    /// </summary>
    /// <param name="port">The connected port object from WebExtensions.Net.</param>
    Task HandleConnectAsync(WebExtensions.Net.Runtime.Port port);

    /// <summary>
    /// Handles tab removal to clean up associated PortSessions.
    /// Called from BackgroundWorker.OnTabRemovedAsync.
    /// </summary>
    /// <param name="tabId">The ID of the removed tab.</param>
    Task HandleTabRemovedAsync(int tabId);

    /// <summary>
    /// Sends SW_RESTARTED message to all tabs to trigger ContentScript reconnection.
    /// Called during BackgroundWorker initialization after service worker restart.
    /// </summary>
    Task NotifyContentScriptsOfRestartAsync();

    /// <summary>
    /// Gets a PortSession by its ID.
    /// </summary>
    /// <param name="portSessionId">The PortSession ID.</param>
    /// <returns>The PortSession if found, null otherwise.</returns>
    PortSession? GetPortSession(string portSessionId);

    /// <summary>
    /// Gets a PortSession by tab key (tabId:frameId).
    /// </summary>
    /// <param name="tabId">The tab ID.</param>
    /// <param name="frameId">The frame ID (defaults to 0 for main frame).</param>
    /// <returns>The PortSession if found, null otherwise.</returns>
    PortSession? GetPortSessionByTab(int tabId, int frameId = 0);

    /// <summary>
    /// Sends a strongly-typed port message to a specific port by its ID.
    /// </summary>
    /// <typeparam name="T">The port message type.</typeparam>
    /// <param name="portId">The port ID to send to.</param>
    /// <param name="message">The port message to send.</param>
    Task SendToPortAsync<T>(string portId, T message) where T : PortMessage;

    /// <summary>
    /// Sends a strongly-typed port message to the ContentScript port of a PortSession.
    /// </summary>
    /// <typeparam name="T">The port message type.</typeparam>
    /// <param name="portSessionId">The PortSession ID.</param>
    /// <param name="message">The port message to send.</param>
    Task SendToContentScriptAsync<T>(string portSessionId, T message) where T : PortMessage;

    /// <summary>
    /// Sends a strongly-typed port message to all App ports attached to a PortSession.
    /// </summary>
    /// <typeparam name="T">The port message type.</typeparam>
    /// <param name="portSessionId">The PortSession ID.</param>
    /// <param name="message">The port message to send.</param>
    Task SendToAppsAsync<T>(string portSessionId, T message) where T : PortMessage;

    /// <summary>
    /// Sets the pending popup tab ID, used to provide the originating tab
    /// in the READY response when an App connects.
    /// Called from BackgroundWorker.OnActionClicked before the popup opens.
    /// </summary>
    /// <param name="tabId">The tab ID that was active when the action was clicked.</param>
    void SetPendingPopupTabId(int tabId);

    /// <summary>
    /// Gets the number of active PortSessions (for diagnostics).
    /// </summary>
    int ActivePortSessionCount { get; }

    /// <summary>
    /// Gets the number of active port connections (for diagnostics).
    /// </summary>
    int ActivePortCount { get; }

    /// <summary>
    /// Registers a handler for RPC requests from ContentScript.
    /// Only one handler can be registered at a time.
    /// </summary>
    /// <param name="handler">The handler delegate to invoke when an RPC request is received from ContentScript.</param>
    void RegisterContentScriptRpcHandler(RpcRequestHandler handler);

    /// <summary>
    /// Registers a handler for RPC requests from App (popup/tab/sidepanel).
    /// Only one handler can be registered at a time.
    /// </summary>
    /// <param name="handler">The handler delegate to invoke when an RPC request is received from App.</param>
    void RegisterAppRpcHandler(RpcRequestHandler handler);

    /// <summary>
    /// Registers a handler for EVENT messages (fire-and-forget notifications).
    /// Only one handler can be registered at a time.
    /// </summary>
    /// <param name="handler">The handler delegate to invoke when an EVENT message is received.</param>
    void RegisterEventHandler(PortEventMessageHandler handler);

    /// <summary>
    /// Sends an RPC response to a specific port.
    /// </summary>
    /// <param name="portId">The port ID to send to.</param>
    /// <param name="portSessionId">The PortSession ID for the response.</param>
    /// <param name="requestId">The original request ID being responded to.</param>
    /// <param name="result">The result object (if success).</param>
    /// <param name="errorMessage">The error message (if failure).</param>
    Task SendRpcResponseAsync(string portId, string portSessionId, string requestId, object? result = null, string? errorMessage = null);

    /// <summary>
    /// Sends an event to the ContentScript of a PortSession.
    /// </summary>
    /// <param name="portSessionId">The PortSession ID.</param>
    /// <param name="eventName">The event name.</param>
    /// <param name="data">Optional event data.</param>
    Task SendEventToContentScriptAsync(string portSessionId, string eventName, object? data = null);

    /// <summary>
    /// Sends an event to all App ports attached to a PortSession.
    /// </summary>
    /// <param name="portSessionId">The PortSession ID.</param>
    /// <param name="eventName">The event name.</param>
    /// <param name="data">Optional event data.</param>
    Task SendEventToAppsAsync(string portSessionId, string eventName, object? data = null);

    /// <summary>
    /// Broadcasts an event to ALL connected App ports across all PortSessions.
    /// Used for system-wide notifications like lock/unlock events.
    /// </summary>
    /// <param name="eventName">The event name.</param>
    /// <param name="data">Optional event data.</param>
    Task BroadcastEventToAllAppsAsync(string eventName, object? data = null);

    /// <summary>
    /// Gets a PortSession by port ID.
    /// </summary>
    /// <param name="portId">The port ID.</param>
    /// <returns>The PortSession if found, null otherwise.</returns>
    PortSession? GetPortSessionByPortId(string portId);

    /// <summary>
    /// Checks if there is an active port session for the given tab.
    /// </summary>
    /// <param name="tabId">The tab ID to check.</param>
    /// <param name="frameId">The frame ID (defaults to 0 for main frame).</param>
    /// <returns>True if an active port session exists for the tab, false otherwise.</returns>
    bool HasActivePortSessionForTab(int tabId, int frameId = 0);

    /// <summary>
    /// Cleans up all pending requests with a custom error message.
    /// Used when session locks due to inactivity or other session-wide events.
    /// Sends cancel RPC responses to ContentScripts and removes requests from storage.
    /// </summary>
    /// <param name="errorMessage">The error message to send to ContentScripts.</param>
    Task CleanupAllPendingRequestsAsync(string errorMessage);
}
