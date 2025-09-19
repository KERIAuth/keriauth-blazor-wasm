using Blazor.BrowserExtension;
using Extension.Models;
using Extension.Services;
using Extension.Services.SignifyService;
using JsBind.Net;
using Microsoft.JSInterop;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using WebExtensions.Net;

namespace Extension;

/// <summary>
/// Background worker for the browser extension, handling message routing between
/// content scripts, the Blazor app, and KERIA services.
/// </summary>




public partial class BackgroundWorker : BackgroundWorkerBase, IDisposable {

    // Constants
    private const string ContentScriptPortPattern = @"^[0-9a-f]{8}-[0-9a-f]{8}-[0-9a-f]{8}-[0-9a-f]{8}$";
    private const string BlazorAppPortPrefix = "blazorAppPort";
    private const string UninstallUrl = "https://keriauth.com/uninstall.html";
    private const string DefaultVersion = "unknown";
    
    // Install reasons
    private const string InstallReason = "install";
    private const string UpdateReason = "update";
    private const string ChromeUpdateReason = "chrome_update";
    private const string SharedModuleUpdateReason = "shared_module_update";
    
    // Message types
    private const string LockAppAction = "LockApp";
    private const string SystemLockDetectedAction = "systemLockDetected";
    private const string FromServiceWorkerType = "fromServiceWorker";

    private readonly ILogger<BackgroundWorker> _logger;
    private readonly IJSRuntime _jsRuntime;
    private readonly IJsRuntimeAdapter _jsRuntimeAdapter;
    private readonly IStorageService _storageService;
    private readonly ISignifyClientService _signifyService;
    private readonly IWebsiteConfigService _websiteConfigService;
    private readonly WebExtensionsApi _webExtensionsApi;

    // Port connections tracking
    private readonly ConcurrentDictionary<string, CsConnection> _pageCsConnections = new();
    private readonly DotNetObjectReference<BackgroundWorker>? _dotNetObjectRef;

    [BackgroundWorkerMain]
    public override void Main() {
        // The build-generated backgroundWorker.js invokes the following content as js-equivalents
        WebExtensions.Runtime.OnInstalled.AddListener(OnInstalledAsync);
        WebExtensions.Runtime.OnStartup.AddListener(OnStartupAsync);
        WebExtensions.Runtime.OnConnect.AddListener(OnConnectAsync);
        WebExtensions.Runtime.OnMessage.AddListener(OnMessageAsync);
        WebExtensions.Alarms.OnAlarm.AddListener(OnAlarmAsync);
        WebExtensions.Action.OnClicked.AddListener(OnActionClickedAsync);
        WebExtensions.Tabs.OnRemoved.AddListener(OnTabRemovedAsync);
        WebExtensions.Runtime.OnSuspend.AddListener(OnSuspendAsync);
        WebExtensions.Runtime.OnSuspendCanceled.AddListener(OnSuspendCanceledAsync);
        // TODO P2 WebExtensions.WebNavigation.OnCompleted.AddListener(OnWebNavCompletedAsync);
        //   Parameters: details - Object with tabId, frameId, url, processId, timeStamp
        // TODO P2 WebExtensions.WebRequest.OnCompleted.AddListener(OnWebReqCompletedAsync);
        //   Parameters: details - Object with requestId, url, method, frameId, tabId, type, timeStamp, etc.
        // TODO P3 WebExtensions.Runtime.OnMessageExternal.AddListener(OnMessageExternalAsync);
        //   Parameters: message (any), sender (MessageSender), sendResponse (function)
        // TODO P2 chrome.webRequest.onBeforeRequest // (network/page events)
        //   Parameters: details - Object with requestId, url, method, frameId, tabId, type, timeStamp, requestBody
    }

    // DEPRECATED: This method is no longer used - see OnInstalledAsync() instead
    // Kept for reference only
    private async Task OnInstalled() {
        _logger.LogInformation("BW: OnInstalled...");
        var indexPageUrl = WebExtensions.Runtime.GetURL("index.html");

        var tab = await WebExtensions.Tabs.Create(new() {
            Url = indexPageUrl
        });
        if (tab is not null) {
            tab.Active = true;
        }
        else {
            throw new ArgumentException("indexPageUrl");
        }
    }

    public BackgroundWorker(
        ILogger<BackgroundWorker> logger,
        IJSRuntime jsRuntime,
        IJsRuntimeAdapter jsRuntimeAdapter,
        IStorageService storageService,
        ISignifyClientService signifyService,
        IWebsiteConfigService websiteConfigService) {
        _logger = logger;
        _jsRuntime = jsRuntime;
        _jsRuntimeAdapter = jsRuntimeAdapter;
        _storageService = storageService;
        _signifyService = signifyService;
        _websiteConfigService = websiteConfigService;
        _webExtensionsApi = new WebExtensionsApi(_jsRuntimeAdapter);
        _dotNetObjectRef = DotNetObjectReference.Create(this);
    }

    // OnInstalled fires when the extension is first installed, updated, or Chrome is updated. Good for setup tasks (e.g., initialize storage, create default rules).
    // Parameter: details - OnInstalledDetails with reason, previousVersion, and id
    [JSInvokable]
    public async Task OnInstalledAsync(object detailsObj) {
        try {
            _logger.LogDebug("OnInstalledAsync event handler called");
            _logger.LogInformation("Extension installed/updated event received");

            // Deserialize to strongly-typed object
            var detailsJson = JsonSerializer.Serialize(detailsObj);
            var details = JsonSerializer.Deserialize<OnInstalledDetails>(detailsJson);
            
            if (details == null) {
                _logger.LogWarning("Failed to deserialize installation details");
                return;
            }

            // TODO P3 set a URL that Chrome will open when the extension is uninstalled. Typically used for surveys or cleanup instructions
            await WebExtensions.Runtime.SetUninstallURL(UninstallUrl);

            _logger.LogInformation("Extension installed/updated: {Reason}", details.Reason);

            switch (details.Reason) {
                case InstallReason:
                    await HandleInstallAsync();
                    break;
                case UpdateReason:
                    await HandleUpdateAsync(details.PreviousVersion ?? DefaultVersion);
                    break;
                case ChromeUpdateReason:
                case SharedModuleUpdateReason:
                default:
                    _logger.LogDebug("Unhandled install reason: {Reason}", details.Reason);
                    break;
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling onInstalled event");
            throw;
        }
    }

    // onStartup fires when Chrome launches with a profile (not incognito) that has the extension installed
    // Typical use: Re-initialize or restore state, reconnect services, refresh caches.
    // Parameters: none
    [JSInvokable]
    public async Task OnStartupAsync() {
        try {
            _logger.LogDebug("OnStartupAsync event handler called");
            _logger.LogInformation("Browser startup detected - reinitializing background worker");

            // TODO P2 Reinitialize inactivity timer?
            // TODO P2 add a lock icon to the extension icon

            _logger.LogInformation("Background worker reinitialized on browser startup");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling onStartup event");
            throw;
        }
    }

    // onConnect fires when a connection is made from content scripts or other extension pages
    // Parameter: port - Port object with name and sender properties
    [JSInvokable]
    public async Task OnConnectAsync(object portObj) {
        try {
            _logger.LogDebug("OnConnectAsync event handler called");
            
            // Deserialize to strongly-typed object
            var portJson = JsonSerializer.Serialize(portObj);
            var port = JsonSerializer.Deserialize<Port>(portJson);
            
            if (port == null) {
                _logger.LogWarning("Failed to deserialize port connection");
                return;
            }

            var connectionId = port.Name ?? Guid.NewGuid().ToString();
            _logger.LogInformation("Port connected: {Name}", connectionId);

            var tabId = port.Sender?.Tab?.Id ?? -1;
            var origin = port.Sender?.Origin ?? port.Sender?.Url ?? "unknown";

            // Check if this is a content script port (UUID pattern)
            if (Regex.IsMatch(connectionId, ContentScriptPortPattern)) {
                await HandleContentScriptConnectionAsync(connectionId, portObj, tabId);
            }
            else if (connectionId.StartsWith(BlazorAppPortPrefix, StringComparison.OrdinalIgnoreCase)) {
                await HandleBlazorAppConnectionAsync(connectionId, portObj, tabId, origin);
            }
            else {
                _logger.LogWarning("Unknown port connection: {ConnectionId}", connectionId);
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling port connection");
        }
    }


    // OnMessage fires when extension parts or external apps send messages.
    // Typical use: Coordination, data requests.
    // Returns: Response to send back to the message sender (or null)
    [JSInvokable]
    public async Task<object?> OnMessageAsync(object messageObj, object senderObj) {
        try {
            _logger.LogDebug("OnMessageAsync event handler called");
            
            // Deserialize sender to strongly-typed object
            var senderJson = JsonSerializer.Serialize(senderObj);
            var sender = JsonSerializer.Deserialize<MessageSender>(senderJson);
            
            // Deserialize message as RuntimeMessage
            var messageJson = JsonSerializer.Serialize(messageObj);
            var message = JsonSerializer.Deserialize<RuntimeMessage>(messageJson);
            
            if (message?.Action != null) {
                _logger.LogDebug("Runtime message received: {Action} from {SenderId}", message.Action, sender?.Id);

                return message.Action switch {
                    // "resetInactivityTimer" => await ResetInactivityTimerAsync(),
                    LockAppAction => await HandleLockAppMessageAsync(),
                    SystemLockDetectedAction => await HandleSystemLockDetectedAsync(),
                    _ => await HandleUnknownMessageAsync(message.Action)
                };
            }

            return null;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling runtime message");
            return null;
        }
    }

    // onAlarm fires at a scheduled interval/time.
    // Typical use: Periodic tasks, background sync
    [JSInvokable]
    public async Task OnAlarmAsync(object alarmObj) {
        try {
            _logger.LogDebug("OnAlarmAsync event handler called");
            _logger.LogInformation("LIFECYCLE: Background worker reactivated by alarm event at {Timestamp}", DateTime.UtcNow);
            
            // Deserialize to strongly-typed object
            var alarmJson = JsonSerializer.Serialize(alarmObj);
            var alarm = JsonSerializer.Deserialize<Alarm>(alarmJson);
            
            if (alarm != null) {
                _logger.LogInformation("SECURITY: Alarm '{AlarmName}' fired - processing security action", alarm.Name);
                
                // Convert scheduledTime from milliseconds to DateTime if needed
                var scheduledDateTime = DateTimeOffset.FromUnixTimeMilliseconds((long)alarm.ScheduledTime).UtcDateTime;
                _logger.LogDebug("Alarm scheduled for {ScheduledTime}, period: {Period} minutes", 
                    scheduledDateTime, alarm.PeriodInMinutes ?? 0);

                // The InactivityTimerService handles the actual alarm logic through its own listener
                _logger.LogDebug("Alarm event will be processed by InactivityTimerService");
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling alarm");
        }
    }

    // onClicked event fires when user clicks the extension's toolbar icon.
    // Typical use: Open popup, toggle feature, inject script
    // Note that the default_action is intentionally not defined in our manifest.json, since the click event handling will be dependant on UX state
    [JSInvokable]
    public async Task OnActionClickedAsync(object tabObj) {
        try {
            _logger.LogDebug("OnActionClickedAsync event handler called");
            
            // Deserialize to strongly-typed object
            var tabJson = JsonSerializer.Serialize(tabObj);
            var tab = JsonSerializer.Deserialize<Tab>(tabJson);
            
            if (tab == null) {
                _logger.LogWarning("Failed to deserialize tab information");
                await CreateExtensionTabAsync();
                return;
            }

            _logger.LogInformation("Action button clicked on tab: {TabId}, URL: {Url}", tab.Id, tab.Url);

            if (tab.Id > 0 && !string.IsNullOrEmpty(tab.Url) && tab.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) {
                await HandleActionClickOnWebPageAsync(tab.Id, tab.Url, tab.PendingUrl);
            }
            else {
                await CreateExtensionTabAsync();
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling action click");
        }
    }

    // onRemoved fires when a tab is closed
    [JSInvokable]
    public async Task OnTabRemovedAsync(int tabId, object removeInfoObj) {
        try {
            _logger.LogDebug("OnTabRemovedAsync event handler called");
            
            // Deserialize to strongly-typed object
            var removeInfoJson = JsonSerializer.Serialize(removeInfoObj);
            var removeInfo = JsonSerializer.Deserialize<TabRemoveInfo>(removeInfoJson);
            
            _logger.LogDebug("Tab removed: {TabId}, WindowId: {WindowId}, WindowClosing: {WindowClosing}", 
                tabId, removeInfo?.WindowId, removeInfo?.IsWindowClosing);

            // Remove any connections associated with this tab
            var connectionToRemove = _pageCsConnections
                .Where(kvp => kvp.Value.TabId == tabId)
                .Select(kvp => kvp.Key)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(connectionToRemove)) {
                _pageCsConnections.TryRemove(connectionToRemove, out _);
                _logger.LogDebug("Removed connection for closed tab: {TabId}", tabId);
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling tab removal");
        }
    }

    // webNavigation.onComplete event fires when tabs navigate.
    // Typical use: Inject scripts, block/redirect, monitor
    public static async Task OnWebNavCompletedAsync() {
        await Task.Delay(0);
    }

    // webRequest.onCompleted event fires when webRequests are made and finish.
    // Typical use: Inject scripts, block/redirect, monitor
    public static async Task OnWebReqCompletedAsync() {
        await Task.Delay(0);
    }

    // onSuspend event fires just before the background worker is unloaded (idle ~30s).
    // Typical use: Save in-memory state, cleanup, flush logs. ... quickly (though you get very little time).
    // Parameters: none
    [JSInvokable]
    public async Task OnSuspendAsync() {
        try {
            // TODO P2 needs implementation
            ;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling onSuspend");
        }
    }

    // OnSuspendCanceled event fires if a previously pending unload is canceled (e.g., because a new event kept the worker alive).
    // Parameters: none
    [JSInvokable]
    public async Task OnSuspendCanceledAsync() {
        try {
            // TODO P2 needs implementation
            ;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling onSuspend");
        }
    }

    private async Task HandleInstallAsync() {
        try {
            var installUrl = _webExtensionsApi.Runtime.GetURL("index.html") + "?environment=tab&reason=install";
            var cp = new WebExtensions.Net.Tabs.CreateProperties {
                Url = installUrl
            };
            var res = await WebExtensions.Tabs.Create(cp) ?? throw new AggregateException("could not create tab");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling install");
            throw;
        }
    }

    private async Task HandleUpdateAsync(string previousVersion) {
        try {
            // Note this will be triggered by a Chrome Web Store push,
            // or, when sideloading in development, by installing an updated release per the manifest or a Refresh in DevTools.
            var currentVersion = WebExtensions.Runtime.GetManifest().GetProperty("version").ToString() ?? DefaultVersion;
            _logger.LogInformation("Extension updated from {Previous} to {Current}", previousVersion, currentVersion);

            var updateDetails = new UpdateDetails {
                Reason = UpdateReason,
                PreviousVersion = previousVersion,
                CurrentVersion = currentVersion,
                Timestamp = DateTime.UtcNow.ToString("O")
            };
            await WebExtensions.Storage.Local.Set(new { UpdateDetails = updateDetails });

            var updateUrl = _webExtensionsApi.Runtime.GetURL("index.html") + "?environment=tab&reason=update";
            var cp = new WebExtensions.Net.Tabs.CreateProperties {
                Url = updateUrl
            };
            var res = await WebExtensions.Tabs.Create(cp) ?? throw new AggregateException("could not create tab");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling update");
        }
    }

    private async Task HandleContentScriptConnectionAsync(string connectionId, object port, int tabId) {
        try {
            _logger.LogDebug("HandleContentScriptConnectionAsync called for {ConnectionId}", connectionId);
            _pageCsConnections[connectionId] = new CsConnection {
                Port = port,
                TabId = tabId,
                PageAuthority = "?"
            };

            _logger.LogDebug("Content script connected: {ConnectionId}, TabId: {TabId}", connectionId, tabId);

            // Set up message listener for this content script
            await SetUpPortMessageListenerAsync(port, connectionId, true);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling content script connection");
        }
    }

    private async Task HandleBlazorAppConnectionAsync(string connectionId, object port, int tabId, string origin) {
        try {
            _logger.LogDebug("HandleBlazorAppConnectionAsync called for {ConnectionId}", connectionId);
            var authority = GetAuthorityFromUrl(origin);

            _pageCsConnections[connectionId] = new CsConnection {
                Port = port,
                TabId = tabId,
                PageAuthority = authority
            };

            _logger.LogDebug("Blazor app connected: {ConnectionId}, Authority: {Authority}", connectionId, authority);

            // Set up message listener for this app
            await SetUpPortMessageListenerAsync(port, connectionId, false);

            // Send initial connection message
            var message = new PortMessage(
                Type: FromServiceWorkerType,
                Data: "Service worker connected"
            );
            await SendPortMessageAsync(port, message);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling Blazor app connection");
        }
    }

    private async Task<object?> HandleUnknownMessageAsync(string? action) {
        _logger.LogDebug("HandleUnknownMessageAsync called for action: {Action}", action);
        _logger.LogWarning("Unknown message action: {Action}", action);
        await Task.CompletedTask;
        return null;
    }

    private async Task<object> HandleLockAppMessageAsync() {
        try {
            _logger.LogDebug("HandleLockAppMessageAsync called");
            _logger.LogInformation("Lock app message received - app should be locked");

            // The InactivityTimerService handles the actual locking logic
            _logger.LogDebug("App lock request processed");
            return new { success = true, message = "App locked" };
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling lock app message");
            return new { success = false, error = ex.Message };
        }
    }

    private async Task<object> HandleSystemLockDetectedAsync() {
        try {
            _logger.LogDebug("HandleSystemLockDetectedAsync called");
            _logger.LogWarning("System lock/suspend/hibernate detected in background worker");

            // Send lock message to all connected tabs/apps
            try {
                await _webExtensionsApi.Runtime.SendMessage(new { action = LockAppAction });
                _logger.LogDebug("Sent LockApp message due to system lock detection");
            }
            catch (Exception ex) {
                _logger.LogDebug(ex, "Could not send LockApp message (expected if no pages open)");
            }

            return new { success = true, message = "System lock detected and app locked" };
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling system lock detection");
            return new { success = false, error = ex.Message };
        }
    }

    private async Task HandleActionClickOnWebPageAsync(int tabId, string url, string? pendingUrl) {
        try {
            var targetUrl = pendingUrl ?? url;
            var origin = new Uri(targetUrl).GetLeftPart(UriPartial.Authority) + "/";

            _logger.LogInformation("Handling action click for origin: {Origin}", origin);

            // Check if the extension has permission to access the tab (based on its origin), and if not, request it
            var hasPermission = await CheckOriginPermissionAsync(origin);
            _logger.LogInformation("Origin permission check result for {Origin}: {HasPermission}", origin, hasPermission);

            if (!hasPermission) {
                // Request permission from the user
                var isGranted = await RequestOriginPermissionAsync(origin);
                if (isGranted) {
                    _logger.LogInformation("Permission granted for: {Origin}", origin);
                    await UseActionPopupAsync(tabId);
                } else {
                    _logger.LogInformation("Permission denied for: {Origin}", origin);
                    await CreateExtensionTabAsync();
                }
            } else {
                // If user clicks on the action icon on a page already allowed permission, 
                // but for an interaction not initiated from the content script
                await CreateExtensionTabAsync();
                // TODO P2: Consider implementing useActionPopup(tabId) for direct popup interaction
            }

            // Clear the popup url for the action button, if it is set, 
            // so that future use of the action button will also trigger this same handler
            await WebExtensions.Action.SetPopup(new() { Popup = "" });
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling action click on web page");
        }
    }

    private async Task<bool> CheckOriginPermissionAsync(string origin) {
        try {
            _logger.LogDebug("Checking permission for origin: {Origin}", origin);

            // Use JavaScript helper module for permissions (CSP-compliant)
            // NOTE: WebExtensions.Net.Permissions API has type conversion issues - see RequestOriginPermissionAsync for details
            var permissionsModule = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./scripts/es6/PermissionsHelper.js");
            var hasPermission = await permissionsModule.InvokeAsync<bool>("PermissionsHelper.contains", 
                new { origins = new[] { origin } });
            
            _logger.LogDebug("Permission check result for {Origin}: {HasPermission}", origin, hasPermission);
            return hasPermission;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error checking origin permission for {Origin}", origin);
            return false;
        }
    }

    private async Task<bool> RequestOriginPermissionAsync(string origin) {
        try {
            _logger.LogDebug("Requesting permission for origin: {Origin}", origin);
            
            // Use JavaScript helper module for permissions (CSP-compliant)
            // NOTE: WebExtensions.Net.Permissions API has type conversion issues:
            // - MatchPattern constructor ambiguity (Restricted vs Unrestricted)
            // - Type mismatches between PermissionsType and AnyPermissions
            // - String[] to IEnumerable<MatchPattern> conversion problems
            // The JS module approach avoids these complexities while remaining secure.
            var permissionsModule = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./scripts/es6/PermissionsHelper.js");
            var isGranted = await permissionsModule.InvokeAsync<bool>("PermissionsHelper.request", 
                new { origins = new[] { origin } });
            
            _logger.LogDebug("Permission request result for {Origin}: {IsGranted}", origin, isGranted);
            return isGranted;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error requesting origin permission for {Origin}", origin);
            return false;
        }
    }

    private async Task UseActionPopupAsync(int tabId) {
        try {
            _logger.LogDebug("Using action popup for tab: {TabId}", tabId);

            // Set the popup for this specific tab to show the extension interface
            await WebExtensions.Action.SetPopup(new() {
                Popup = "index.html?environment=popup"
                // Note: TabId may not be supported in SetPopupDetails - this will set for all tabs
            });

            _logger.LogDebug("Action popup configured for tab: {TabId}", tabId);
            // Programmatically open the popup by simulating a click on the action button
            WebExtensions.Action.OpenPopup();
            _logger.LogDebug("Action popup opened for tab: {TabId}", tabId);
            // clear the popup after use, so future clicks trigger the OnActionClicked handler again
            await WebExtensions.Action.SetPopup(new() { Popup = "" });
            _logger.LogDebug("Action popup cleared for future clicks on tab: {TabId}", tabId);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error setting up action popup for tab {TabId}", tabId);
        }
    }

    private async Task CreateExtensionTabAsync() {
        try {
            var tabUrl = _webExtensionsApi.Runtime.GetURL("index.html") + "?environment=tab";
            var cp = new WebExtensions.Net.Tabs.CreateProperties {
                Url = tabUrl
                // TODO P2 use the same tab identifier, so we don't get multiple tabs
            };
            var res = await WebExtensions.Tabs.Create(cp) ?? throw new AggregateException("could not create tab");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error creating extension tab");
        }
    }

    private async Task SetUpPortMessageListenerAsync(object port, string connectionId, bool isContentScript) {
        try {
            _logger.LogDebug("SetUpPortMessageListenerAsync called for {ConnectionId}", connectionId);
            // This would typically involve setting up JavaScript interop to listen for port messages
            // For now, we'll implement this as a placeholder
            _logger.LogDebug("Set up port message listener for {ConnectionId}, IsContentScript: {IsContentScript}",
                connectionId, isContentScript);
            await Task.CompletedTask;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error setting up port message listener");
        }
    }

    private async Task SendPortMessageAsync(object port, object message) {
        try {
            _logger.LogDebug("SendPortMessageAsync called");
            // This would involve JavaScript interop to send messages through the port
            _logger.LogDebug("Sent port message: {Message}", JsonSerializer.Serialize(message));
            await Task.CompletedTask;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error sending port message");
        }
    }


    private string GetAuthorityFromUrl(string url) {
        try {
            if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
                return uri.Authority;
            }
        }
        catch (Exception ex) {
            _logger.LogDebug(ex, "Error extracting authority from URL: {Url}", url);
        }

        return "unknown";
    }

    // Supporting classes
    private sealed class CsConnection {
        public object Port { get; set; } = default!;
        public int TabId { get; set; }
        public string PageAuthority { get; set; } = "?";
    }

    private sealed class UpdateDetails {
        public string Reason { get; set; } = "";
        public string PreviousVersion { get; set; } = "";
        public string CurrentVersion { get; set; } = "";
        public string Timestamp { get; set; } = "";
    }

    public void Dispose() {
        try {
            _dotNetObjectRef?.Dispose();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error disposing BackgroundWorker");
        }
        GC.SuppressFinalize(this);
    }
}
