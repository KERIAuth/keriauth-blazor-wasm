using Blazor.BrowserExtension;
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
    // TODO P1 logging doesn't work here because Program.cs or App hasn't created these dependency injections?
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

    // Constants
    private const string InactivityAlarmName = "inactivityAlarm";
    private const double DefaultInactivityTimeoutMinutes = 5.0;

    [BackgroundWorkerMain]
    public override void Main() {
        _logger.LogTrace("Worker started at {time}", DateTimeOffset.UtcNow);
        _logger.LogInformation("Worker started at {time}", DateTimeOffset.UtcNow);
        WebExtensions.Runtime.OnInstalled.AddListener(OnInstalled);
    }

    private async Task OnInstalled() {
        _logger.LogInformation("BW: OnInstalled...");
        var indexPageUrl = WebExtensions.Runtime.GetURL("index.html");
        
        var tab = await WebExtensions.Tabs.Create(new() {
            Url = indexPageUrl
        });
        if (tab is not null) {
            tab.Active = true;
        } else {
#pragma warning disable CA2201 // Do not raise reserved exception types
            throw new ApplicationException("indexPageUrl");
#pragma warning restore CA2201 // Do not raise reserved exception types
                              // _logger.LogWarning("could not create index page");
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

    /// <summary>
    /// Initialize the background worker and set up event listeners
    /// </summary>
    public async Task InitializeAsync() {
        await _jsRuntime.InvokeVoidAsync("console.warn", $"Initializing...");
        _logger.LogInformation("Initializing");
        _logger.LogWarning("Initializing");
        _logger.LogError("Initializing");

        try {
            // Set up Chrome extension event listeners
            SetUpEventListeners();
            _logger.LogInformation("Initialized");
            await Task.CompletedTask;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to initialize BackgroundWorker");
            throw;
        }
    }

    private void SetUpEventListeners() {
        try {
            // Listen for extension installation/update
            _webExtensionsApi.Runtime.OnInstalled.AddListener(OnInstalledAsync);
            _logger.LogDebug("Added onInstalled listener");

            // Listen for browser startup
            _webExtensionsApi.Runtime.OnStartup.AddListener(OnStartupAsync);
            _logger.LogDebug("Added onStartup listener");

            // Listen for port connections
            _webExtensionsApi.Runtime.OnConnect.AddListener(OnConnectAsync);
            _logger.LogDebug("Added onConnect listener");

            // Listen for runtime messages (non-port)
            _webExtensionsApi.Runtime.OnMessage.AddListener(OnMessageAsync);
            _logger.LogDebug("Added onMessage listener");

            // Listen for alarm events
            _webExtensionsApi.Alarms.OnAlarm.AddListener(OnAlarmAsync);
            _logger.LogDebug("Added onAlarm listener");

            // Listen for action button clicks
            _webExtensionsApi.Action.OnClicked.AddListener(OnActionClickedAsync);
            _logger.LogDebug("Added onActionClicked listener");

            // Listen for tab removal
            _webExtensionsApi.Tabs.OnRemoved.AddListener(OnTabRemovedAsync);
            _logger.LogDebug("Added onTabRemoved listener");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error setting up event listeners");
            throw;
        }
    }

    [JSInvokable]
    public async Task OnInstalledAsync(object details) {
        try {
            _logger.LogInformation("Extension installed/updated event received");
            await _jsRuntime.InvokeVoidAsync("console.warn", $"OnInstalledAsync...");

            var detailsJson = JsonSerializer.Serialize(details);
            var detailsDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(detailsJson);

            if (detailsDict?.TryGetValue("reason", out var reasonElement) == true) {
                var reason = reasonElement.GetString();
                _logger.LogInformation("Extension installed/updated: {Reason}", reason);

                switch (reason) {
                    case "install":
                        await HandleInstallAsync();
                        break;
                    case "update":
                        await HandleUpdateAsync(detailsDict);
                        break;
                    case "chrome_update":
                    case "shared_module_update":
                    default:
                        _logger.LogDebug("Unhandled install reason: {Reason}", reason);
                        break;
                }
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling onInstalled event");
            throw;
        }
    }

    [JSInvokable]
    public async Task OnStartupAsync() {
        try {
            await _jsRuntime.InvokeVoidAsync("console.warn", $"OnStartupAsync...");
            _logger.LogInformation("Browser startup detected");
            // This handler could potentially be used to set the extension's icon to a "locked" state
            await Task.CompletedTask;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling onStartup event");
            throw;
        }
    }

    [JSInvokable]
    public async Task OnConnectAsync(object port) {
        try {
            await _jsRuntime.InvokeVoidAsync("console.warn", $"OnConnectAsync...");
            var portJson = JsonSerializer.Serialize(port);
            var portDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(portJson);

            if (portDict?.TryGetValue("name", out var nameElement) == true) {
                var connectionId = nameElement.GetString() ?? Guid.NewGuid().ToString();
                _logger.LogInformation("Port connected: {Name}", connectionId);

                var tabId = GetTabIdFromPort(portDict);
                var origin = GetOriginFromPort(portDict);

                // Check if this is a content script port (UUID pattern)
                var csPortPattern = @"^[0-9a-f]{8}-[0-9a-f]{8}-[0-9a-f]{8}-[0-9a-f]{8}$";
                if (Regex.IsMatch(connectionId, csPortPattern)) {
                    await HandleContentScriptConnectionAsync(connectionId, port, tabId);
                }
                else if (connectionId.StartsWith("blazorAppPort", StringComparison.OrdinalIgnoreCase)) {
                    await HandleBlazorAppConnectionAsync(connectionId, port, tabId, origin);
                }
                else {
                    _logger.LogWarning("Unknown port connection: {ConnectionId}", connectionId);
                }
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling port connection");
        }
    }

    [JSInvokable]
    public async Task<object?> OnMessageAsync(object message, object sender) {
        try {
            await _jsRuntime.InvokeVoidAsync("console.warn", $"OnMessageAsync...");
            var messageJson = JsonSerializer.Serialize(message);
            var messageDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(messageJson);

            if (messageDict?.TryGetValue("action", out var actionElement) == true) {
                var action = actionElement.GetString();
                _logger.LogDebug("Runtime message received: {Action}", action);

                return action switch {
                    "resetInactivityTimer" => await ResetInactivityTimerAsync(),
                    _ => await HandleUnknownMessageAsync(action)
                };
            }

            return null;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling runtime message");
            return null;
        }
    }

    [JSInvokable]
    public async Task OnAlarmAsync(object alarm) {
        try {
            await _jsRuntime.InvokeVoidAsync("console.error", $"OnAlarmAsync...");
            var alarmJson = JsonSerializer.Serialize(alarm);
            var alarmDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(alarmJson);

            if (alarmDict?.TryGetValue("name", out var nameElement) == true) {
                var alarmName = nameElement.GetString();
                _logger.LogInformation("Alarm fired: {Name}", alarmName);

                if (alarmName == InactivityAlarmName) {
                    await HandleInactivityTimeoutAsync();
                }
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling alarm");
        }
    }

    [JSInvokable]
    public async Task OnActionClickedAsync(object tab) {
        try {
            await _jsRuntime.InvokeVoidAsync("console.warn", $"OnActionClickedAsync...");
            var tabJson = JsonSerializer.Serialize(tab);
            var tabDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tabJson);

            var tabId = GetTabIdFromTabObject(tabDict);
            var url = GetUrlFromTabObject(tabDict);

            _logger.LogInformation("Action button clicked on tab: {TabId}, URL: {Url}", tabId, url);

            if (tabId > 0 && !string.IsNullOrEmpty(url) && url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) {
                await HandleActionClickOnWebPageAsync(tabId, url, tabDict);
            }
            else {
                await CreateExtensionTabAsync();
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling action click");
        }
    }

    [JSInvokable]
    public async Task OnTabRemovedAsync(int tabId, object removeInfo) {
        try {
            _logger.LogDebug("Tab removed: {TabId}", tabId);
            await _jsRuntime.InvokeVoidAsync("console.warn", $"OnTabRemovedAsync...");

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

    private async Task HandleInstallAsync() {
        try {
            await _jsRuntime.InvokeVoidAsync("console.warn", $"HandleInstallAsync...");

            var installUrl = _webExtensionsApi.Runtime.GetURL("index.html") + "?environment=tab&reason=install";
            await _jsRuntime.InvokeVoidAsync("console.warn", $"Would create install tab: {installUrl}");
            _logger.LogInformation("Install handled");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling install");
            throw;
        }
    }

    private async Task HandleUpdateAsync(Dictionary<string, JsonElement> details) {
        try {
            await _jsRuntime.InvokeVoidAsync("console.warn", $"HandleUpdateAsync...");
            var previousVersion = details.TryGetValue("previousVersion", out var prevElement)
                ? prevElement.GetString() ?? "unknown"
                : "unknown";

            var currentVersion = "unknown";
            try {
                // Note: manifest retrieval currently has issues with JsonElement serialization
                currentVersion = "1.0.0";
            }
            catch {
                // Ignore manifest errors for now
            }

            _logger.LogInformation("Extension updated from {Previous} to {Current}", previousVersion, currentVersion);

            var updateDetails = new UpdateDetails {
                Reason = "update",
                PreviousVersion = previousVersion,
                CurrentVersion = currentVersion,
                Timestamp = DateTime.UtcNow.ToString("O")
            };

            // Store update details using the storage service
            var result = await _storageService.SetItem(updateDetails);
            if (result.IsFailed) {
                var errorMessage = result.Errors.Count > 0 ? result.Errors[0].Message : "Unknown error";
                _logger.LogWarning("Failed to store update details: {Error}", errorMessage);
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling update");
        }
    }

    private async Task HandleContentScriptConnectionAsync(string connectionId, object port, int tabId) {
        try {
            await _jsRuntime.InvokeVoidAsync("console.warn", $"HandleContentScriptConnectionAsync...");
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
            await _jsRuntime.InvokeVoidAsync("console.warn", $"HandleBlazorAppConnectionAsync...");
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
            await SendPortMessageAsync(port, new {
                type = "fromServiceWorker",
                data = "Service worker connected"
            });
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling Blazor app connection");
        }
    }

    private async Task<object> ResetInactivityTimerAsync() {
        try {
            await _jsRuntime.InvokeVoidAsync("console.warn", $"ResetInactivityTimerAsync...");
            var inactivityTimeoutMinutes = DefaultInactivityTimeoutMinutes;

            _logger.LogDebug("Reset inactivity timer: {Minutes} minutes", inactivityTimeoutMinutes);
            return new { success = true, timeout = inactivityTimeoutMinutes };
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error resetting inactivity timer");
            return new { success = false, error = ex.Message };
        }
    }

    private async Task<object?> HandleUnknownMessageAsync(string? action) {
        await _jsRuntime.InvokeVoidAsync("console.warn", $"HandleUnknownMessageAsync...");
        _logger.LogWarning("Unknown message action: {Action}", action);
        await Task.CompletedTask;
        return null;
    }

    private async Task HandleInactivityTimeoutAsync() {
        try {
            await _jsRuntime.InvokeVoidAsync("console.warn", $"HandleInactivityTimeoutAsync...");
            _logger.LogInformation("Inactivity timeout expired - clearing passcode");

            // For now, just log the action
            await _jsRuntime.InvokeVoidAsync("console.warn", "Would remove passcode from session storage");

            _logger.LogDebug("Handled inactivity timeout");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling inactivity timeout");
        }
    }

    private async Task HandleActionClickOnWebPageAsync(int tabId, string url, Dictionary<string, JsonElement>? tabDict) {
        try {
            var pendingUrl = tabDict?.TryGetValue("pendingUrl", out var pendingElement) == true
                ? pendingElement.GetString()
                : null;

            var targetUrl = pendingUrl ?? url;
            var origin = new Uri(targetUrl).GetLeftPart(UriPartial.Authority) + "/";

            _logger.LogInformation("Handling action click for origin: {Origin}", origin);

            // For now, just create an extension tab
            await CreateExtensionTabAsync();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling action click on web page");
        }
    }

    private async Task CreateExtensionTabAsync() {
        try {
            var tabUrl = _webExtensionsApi.Runtime.GetURL("Pages/index.html") + "?environment=tab";
            await _jsRuntime.InvokeVoidAsync("console.warn", $"Would create extension tab: {tabUrl}");
            _logger.LogDebug("Extension tab creation logged");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error creating extension tab");
        }
    }

    private async Task SetUpPortMessageListenerAsync(object port, string connectionId, bool isContentScript) {
        try {
            await _jsRuntime.InvokeVoidAsync("console.warn", $"SetUpPortMessageListenerAsync...");
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
            await _jsRuntime.InvokeVoidAsync("console.warn", $"SendPortMessageAsync...");
            // This would involve JavaScript interop to send messages through the port
            _logger.LogDebug("Sent port message: {Message}", JsonSerializer.Serialize(message));
            await Task.CompletedTask;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error sending port message");
        }
    }

    // Helper methods for extracting data from JavaScript objects
    private int GetTabIdFromPort(Dictionary<string, JsonElement> portDict) {
        try {
            if (portDict.TryGetValue("sender", out var senderElement)) {
                var senderJson = senderElement.GetRawText();
                var senderDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(senderJson);

                if (senderDict?.TryGetValue("tab", out var tabElement) == true) {
                    var tabJson = tabElement.GetRawText();
                    var tabDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tabJson);

                    if (tabDict?.TryGetValue("id", out var idElement) == true) {
                        return idElement.GetInt32();
                    }
                }
            }
        }
        catch (Exception ex) {
            _logger.LogDebug(ex, "Error extracting tab ID from port");
        }

        return -1;
    }

    private string GetOriginFromPort(Dictionary<string, JsonElement> portDict) {
        try {
            if (portDict.TryGetValue("sender", out var senderElement)) {
                var senderJson = senderElement.GetRawText();
                var senderDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(senderJson);

                if (senderDict?.TryGetValue("origin", out var originElement) == true) {
                    return originElement.GetString() ?? "unknown";
                }

                if (senderDict?.TryGetValue("url", out var urlElement) == true) {
                    var url = urlElement.GetString();
                    if (!string.IsNullOrEmpty(url)) {
                        return new Uri(url).GetLeftPart(UriPartial.Authority);
                    }
                }
            }
        }
        catch (Exception ex) {
            _logger.LogDebug(ex, "Error extracting origin from port");
        }

        return "unknown";
    }

    private int GetTabIdFromTabObject(Dictionary<string, JsonElement>? tabDict) {
        try {
            if (tabDict?.TryGetValue("id", out var idElement) == true) {
                return idElement.GetInt32();
            }
        }
        catch (Exception ex) {
            _logger.LogDebug(ex, "Error extracting tab ID from tab object");
        }

        return -1;
    }

    private string GetUrlFromTabObject(Dictionary<string, JsonElement>? tabDict) {
        try {
            if (tabDict?.TryGetValue("url", out var urlElement) == true) {
                return urlElement.GetString() ?? "";
            }
        }
        catch (Exception ex) {
            _logger.LogDebug(ex, "Error extracting URL from tab object");
        }

        return "";
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
        GC.SuppressFinalize(this);
    }
}
