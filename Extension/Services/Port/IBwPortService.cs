using Extension.Models;
using WebExtensions.Net.Runtime;

namespace Extension.Services.Port;

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
    /// Sends a message to a specific port by its ID.
    /// </summary>
    /// <param name="portId">The port ID to send to.</param>
    /// <param name="message">The message object to send.</param>
    Task SendToPortAsync(string portId, object message);

    /// <summary>
    /// Sends a message to the ContentScript port of a PortSession.
    /// </summary>
    /// <param name="portSessionId">The PortSession ID.</param>
    /// <param name="message">The message object to send.</param>
    Task SendToContentScriptAsync(string portSessionId, object message);

    /// <summary>
    /// Sends a message to all App ports attached to a PortSession.
    /// </summary>
    /// <param name="portSessionId">The PortSession ID.</param>
    /// <param name="message">The message object to send.</param>
    Task SendToAppsAsync(string portSessionId, object message);

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
}
