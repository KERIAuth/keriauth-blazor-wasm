using Blazor.BrowserExtension;
using Extension.Helper;
using Extension.Models;
using Extension.Models.ObsoleteExMessages;
using Extension.Services;
using Extension.Services.JsBindings;
using Extension.Services.SignifyService;
using Extension.Services.SignifyService.Models;
using FluentResults;
using JsBind.Net;
using Microsoft.JSInterop;
using System.Text.Json;
using WebExtensions.Net;
using WebExtensions.Net.Manifest;
using WebExtensions.Net.Permissions;
using WebExtensions.Net.Runtime;
using BrowserAlarm = WebExtensions.Net.Alarms.Alarm;
using BrowserTab = WebExtensions.Net.Tabs.Tab;
using RemoveInfo = WebExtensions.Net.Tabs.RemoveInfo;

namespace Extension;

/// <summary>
/// Background worker for the browser extension, handling message routing between
/// content scripts, the Blazor app, and KERIA services.
/// </summary>

public partial class BackgroundWorker : BackgroundWorkerBase, IDisposable {

    // Constants
    // TODO P2 set a real URL that Chrome will open when the extension is uninstalled, to be used for survey or cleanup instructions.
    private const string UninstallUrl = "https://keriauth.com/uninstall.html";
    private const string DefaultVersion = "unknown";

    // Cached JsonSerializerOptions for message deserialization
    private static readonly JsonSerializerOptions PortMessageJsonOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    // Cached JsonSerializerOptions for credential deserialization with increased depth
    // vLEI credentials can have deeply nested structures (edges, rules, chains, etc.)
    private static readonly JsonSerializerOptions CredentialJsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 128  // Increased from default 32 to handle deeply nested vLEI credential structures
    };

    // Cached JsonSerializerOptions for RecursiveDictionary to preserve field ordering for CESR/SAID
    private static readonly JsonSerializerOptions RecursiveDictionaryJsonOptions = new() {
        Converters = { new RecursiveDictionaryConverter() }
    };

    // Message types
    private const string LockAppAction = "LockApp";
    private const string SystemLockDetectedAction = "systemLockDetected";

    private readonly ILogger<BackgroundWorker> _logger;
    private readonly IJSRuntime _jsRuntime;
    private readonly IJsRuntimeAdapter _jsRuntimeAdapter;
    private readonly IStorageService _storageService;
    private readonly ISignifyClientService _signifyService;
    private readonly IWebsiteConfigService _websiteConfigService;
    private readonly WebExtensionsApi _webExtensionsApi;

    // NOTE: No in-memory state tracking needed for runtime.sendMessage approach
    // All state is derived from message sender info or retrieved from persistent storage

    [BackgroundWorkerMain]
    public override void Main() {
        // JavaScript ES modules are loaded by app.ts beforeStart() hook BEFORE Blazor starts
        // The modules are cached in BackgroundWorker's runtime context and available via IJSRuntime
        // Services can import modules using IJSRuntime.InvokeAsync("import", path) - instant from cache

        // The build-generated backgroundWorker.js invokes the following content as js-equivalents
        WebExtensions.Runtime.OnInstalled.AddListener(OnInstalledAsync);
        WebExtensions.Runtime.OnStartup.AddListener(OnStartupAsync);
        WebExtensions.Runtime.OnMessage.AddListener(OnMessageAsync);
        WebExtensions.Alarms.OnAlarm.AddListener(OnAlarmAsync);
        // Don't add an OnClicked handler here because it would be invoked after the one registered in app.ts, and may result in race conditions.
        // WebExtensions.Action.OnClicked.AddListener(OnActionClickedAsync);
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

    private readonly ISignifyClientBinding _signifyClientBinding;

    public BackgroundWorker(
        ILogger<BackgroundWorker> logger,
        IJSRuntime jsRuntime,
        IJsRuntimeAdapter jsRuntimeAdapter,
        IStorageService storageService,
        ISignifyClientService signifyService,
        ISignifyClientBinding signifyClientBinding,
        IWebsiteConfigService websiteConfigService) {
        _logger = logger;
        _jsRuntime = jsRuntime;
        _signifyClientBinding = signifyClientBinding;
        _jsRuntimeAdapter = jsRuntimeAdapter;
        _storageService = storageService;
        _signifyService = signifyService;
        _websiteConfigService = websiteConfigService;
        _webExtensionsApi = new WebExtensionsApi(_jsRuntimeAdapter);
    }

    // onInstalled fires when the extension is first installed, updated, or Chrome is updated. Good for setup tasks (e.g., initialize storage, create default rules).
    // Parameter: details - OnInstalledDetails with reason, previousVersion, and id
    [JSInvokable]
    public async Task OnInstalledAsync(OnInstalledEventCallbackDetails details) {
        try {
            _logger.LogInformation("OnInstalledAsync event handler called");
            _logger.LogInformation("Extension installed/updated event received");


            // TODO P3 set a real URL that Chrome will open when the extension is uninstalled, to be used for survey or cleanup instructions.
            // await WebExtensions.Runtime.SetUninstallURL(UninstallUrl);
            _ = UninstallUrl;

            _logger.LogInformation("Extension installed/updated: {Reason}", details.Reason);

            switch (details.Reason) {
                case OnInstalledReason.Install:
                    await HandleInstallAsync();
                    break;
                case OnInstalledReason.Update:
                    await HandleUpdateAsync(details.PreviousVersion ?? DefaultVersion);
                    break;
                case OnInstalledReason.BrowserUpdate:
                default:
                    _logger.LogInformation("Unhandled install reason: {Reason}", details.Reason);
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
            _logger.LogInformation("OnStartupAsync event handler called");
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

    // onMessage fires when extension parts or external apps send messages.
    // Typical use: Coordination, data requests.
    // Returns: Response to send back to the message sender (or null)
    // [JSInvokable]
    public async Task OnMessageAsync(object messageObj, WebExtensions.Net.Runtime.MessageSender sender) {
        try {
            _logger.LogInformation("OnMessageAsync event handler called");

            // SECURITY: Validate message origin to prevent subdomain attacks
            // Only messages from the extension itself or explicitly permitted origins are allowed
            var isValidSender = await ValidateMessageSenderAsync(sender, messageObj);
            if (!isValidSender) {
                _logger.LogWarning("OnMessageAsync: Message from invalid sender ignored. Sender URL: {Url}, ID: {Id}", sender?.Url, sender?.Id);
                return;
            }

            // Try to deserialize as InboundMessage
            var messageJson = JsonSerializer.Serialize(messageObj);
            _logger.LogDebug("OnMessageAsync messageJson: {MessageJson}", messageJson);
            var inboundMsg = JsonSerializer.Deserialize<ToBwMessage>(messageJson, PortMessageJsonOptions);

            if (inboundMsg?.Type != null) {
                _logger.LogInformation("Message received: {Type} from {Url}", inboundMsg.Type, sender?.Url);

                // Check if message is from extension (App) or from content script
                var isFromExtension = sender?.Url?.StartsWith($"chrome-extension://{WebExtensions.Runtime.Id}", StringComparison.OrdinalIgnoreCase) ?? false;

                // Messages from App that need forwarding to ContentScript
                if (isFromExtension) {
                    var appMsg = JsonSerializer.Deserialize<AppBwMessage>(messageJson, PortMessageJsonOptions);
                    _logger.LogDebug("AppMessage deserialized - TabId: {TabId}", appMsg?.TabId);
                    if (appMsg != null) {
                        await HandleAppMessageAsync(appMsg);
                        return;
                    }
                    else {
                        _logger.LogWarning("Failed to deserialize AppMessage {appMsg}", messageJson);
                        await HandleUnknownMessageActionAsync(inboundMsg.Type);
                        return;
                    }
                }
                else {
                    // Handle ContentScript messages (from web pages)
                    var contentScriptMsg = JsonSerializer.Deserialize<CsBwMessage>(messageJson, PortMessageJsonOptions);
                    if (contentScriptMsg != null) {
                        await HandleContentScriptMessageAsync(contentScriptMsg, sender);
                        return;
                    }
                }

                // Try to deserialize as RuntimeMessage (internal extension messages)
                var message = JsonSerializer.Deserialize<RuntimeMessage>(messageJson);

                if (message?.Action != null) {
                    _logger.LogInformation("Runtime message received: {Action} from {SenderId}", message.Action, sender?.Id);

                    switch (message.Action) {
                        case "resetInactivityTimer":
                            // await ResetInactivityTimerAsync();
                            break;
                        case LockAppAction:
                            await HandleLockAppMessageAsync();
                            break;
                        case SystemLockDetectedAction:
                            await HandleSystemLockDetectedAsync();
                            break;
                        default:
                            await HandleUnknownMessageActionAsync(message.Action);
                            break;
                    }
                    return;
                }
            }
            else {
                _logger.LogWarning("Failed to deserialize InboundMessage.Type {MessageJson}", messageJson);
                return;
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling runtime message");
            return;
        }
    }

    // onAlarm fires at a scheduled interval/time.
    // Typical use: Periodic tasks, background sync
    [JSInvokable]
    public async Task OnAlarmAsync(BrowserAlarm alarm) {
        try {
            _logger.LogInformation("OnAlarmAsync event handler called");
            _logger.LogInformation("LIFECYCLE: Background worker reactivated by alarm event at {Timestamp}", DateTime.UtcNow);

            if (alarm != null) {
                _logger.LogInformation("SECURITY: Alarm '{AlarmName}' fired - processing security action", alarm.Name);

                // Convert scheduledTime from milliseconds to DateTime if needed
                var scheduledDateTime = DateTimeOffset.FromUnixTimeMilliseconds((long)alarm.ScheduledTime).UtcDateTime;
                _logger.LogInformation("Alarm scheduled for {ScheduledTime}, period: {Period} minutes",
                    scheduledDateTime, alarm.PeriodInMinutes ?? 0);

                // The InactivityTimerService handles the actual alarm logic through its own listener
                _logger.LogInformation("Alarm event will be processed by InactivityTimerService");
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling alarm");
        }
    }

    // NOTE: Action click permission handling is now done in app.js beforeStart() hook to preserve user gesture.
    // See app.js for chrome.action.onClicked listener that handles permission requests and script registration.
    //
    // This OnActionClickedAsync method will be invoked after the handler above, since this one is registered after it.
    // Typical use: Open popup, toggle feature, 
    [JSInvokable]
    public async Task OnActionClickedAsync(BrowserTab tab) {
        try {
            _logger.LogInformation("OnActionClickedAsync event handler called");

            // Validate tab information
            if (tab is null || string.IsNullOrEmpty(tab.Url)) {
                _logger.LogWarning("Invalid tab information");
                return;
            }

            _logger.LogInformation("Action button clicked on tab: {TabId}, URL: {Url}", tab.Id, tab.Url);
            // NOTE: Actual permission request and script registration is now handled in app.js

            // 1) Compute per-origin match patterns from the clicked tab
            var matchPatterns = BuildMatchPatternsFromTabUrl(tab.Url);
            if (matchPatterns.Count == 0) {
                _logger.LogInformation("KERIAuth BW: Unsupported or restricted URL scheme; not registering persistence. URL: {Url}", tab.Url);
                return;
            }

            // SECURITY: Check if permissions are already granted for this specific origin.
            // This prevents subdomain attacks - if permission was granted for https://example.com/*,
            // then https://evil.example.com/* will NOT have permission and cannot inject scripts.
            // Only exact origin matches are permitted.
            var anyPermissions = new AnyPermissions {
                Origins = [.. matchPatterns.Select(pattern => new MatchPattern(new MatchPatternRestricted(pattern)))]
            };
            bool granted = false;
            try {
                granted = await WebExtensions.Permissions.Contains(anyPermissions);
                if (granted) {
                    _logger.LogInformation("KERIAuth BW: Persistent host permission already granted for {Patterns}", string.Join(", ", matchPatterns));
                }
                else {
                    _logger.LogInformation("KERIAuth BW: Persistent host permission not granted for {Patterns}. Will inject for current tab only using activeTab.", string.Join(", ", matchPatterns));
                }
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "KERIAuth BW: Could not check persistent host permissions - will use activeTab");
            }

            // NOTE: Content script injection is now handled in app.js beforeStart() hook
            // This preserves the user gesture required for permission requests
            // The JavaScript handler runs before this C# handler and handles all injection logic
            _logger.LogInformation("KERIAuth BW: Content script injection handled by app.js - no action needed in C# handler");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling action click");
        }
    }

    /// <summary>
    /// Build match patterns from the tab's URL, suitable for both:
    /// - chrome.permissions.{contains,request}({ origins: [...] })
    /// - chrome.scripting.registerContentScripts({ matches: [...] })
    ///
    /// Notes:
    /// - Match patterns don't include ports; they'll match any port on that host.
    /// - Only http/https are supported for injection.
    /// </summary>
    private List<string> BuildMatchPatternsFromTabUrl(string tabUrl) {
        try {
            var uri = new Uri(tabUrl);
            if (uri.Scheme == "http" || uri.Scheme == "https") {
                // Exact host only - returns pattern like "https://example.com/*"
                return [$"{uri.Scheme}://{uri.Host}/*"];
            }
        }
        catch (Exception ex) {
            _logger.LogDebug(ex, "Error parsing tab URL: {TabUrl}", tabUrl);
        }
        return [];
    }

    // onRemoved fires when a tab is closed
    [JSInvokable]
    public async Task OnTabRemovedAsync(int tabId, RemoveInfo removeInfo) {
        try {
            _logger.LogInformation("OnTabRemovedAsync event handler called");

            _logger.LogInformation("Tab removed: {TabId}, WindowId: {WindowId}, WindowClosing: {WindowClosing}",
                tabId, removeInfo?.WindowId, removeInfo?.IsWindowClosing);

            // NOTE: No cleanup needed with runtime.sendMessage approach
            // All message handling is stateless - no connection tracking required
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

    // onInstall fires when the extension is installed
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

    // onUpdated fires when the extension is already installed, but updated, which may occur automatically from Chrome and ChromeWebStore, without user intervention
    // This will be triggered by a Chrome Web Store push,
    // or, when sideloading in development, by installing an updated release per the manifest or a Refresh in DevTools.

    private async Task HandleUpdateAsync(string previousVersion) {
        try {
            var currentVersion = WebExtensions.Runtime.GetManifest().GetProperty("version").ToString() ?? DefaultVersion;
            _logger.LogInformation("Extension updated from {Previous} to {Current}", previousVersion, currentVersion);

            var updateDetails = new UpdateDetails {
                Reason = OnInstalledReason.Update.ToString(),
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

    private async Task HandleUnknownMessageActionAsync(string? action) {
        _logger.LogWarning("HandleUnknownMessageActionAsync: Unknown message action: {Action}", action);
        await Task.CompletedTask;
        return;
    }

    private async Task HandleLockAppMessageAsync() {
        try {
            _logger.LogInformation("HandleLockAppMessageAsync called");
            _logger.LogInformation("Lock app message received - app should be locked");

            // The InactivityTimerService handles the actual locking logic
            _logger.LogInformation("App lock request processed");
            return;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling lock app message");
            return;
        }
    }

    private async Task HandleSystemLockDetectedAsync() {
        try {
            _logger.LogInformation("HandleSystemLockDetectedAsync called");
            _logger.LogWarning("System lock/suspend/hibernate detected in background worker");

            // Send lock message to all connected tabs/apps
            try {
                await _webExtensionsApi.Runtime.SendMessage(new { action = LockAppAction });
                _logger.LogInformation("Sent LockApp message due to system lock detection");
            }
            catch (Exception ex) {
                _logger.LogInformation(ex, "Could not send LockApp message (expected if no pages open)");
            }
            return;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling system lock detection");
            return;
        }
    }

    private async Task<bool> CheckOriginPermissionAsync(string origin) {
        try {
            _logger.LogInformation("Checking permission for origin: {Origin}", origin);

            // Use JavaScript helper module for permissions (CSP-compliant)
            // NOTE: WebExtensions.Net.Permissions API has type conversion issues - see RequestOriginPermissionAsync for details
            var permissionsModule = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./scripts/es6/PermissionsHelper.js");
            var hasPermission = await permissionsModule.InvokeAsync<bool>("PermissionsHelper.contains",
                new { origins = new[] { origin } });

            _logger.LogInformation("Permission check result for {Origin}: {HasPermission}", origin, hasPermission);
            return hasPermission;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error checking origin permission for {Origin}", origin);
            return false;
        }
    }

    // TODO P1: is this now done in app.js?
    private async Task<bool> RequestOriginPermissionAsync(string origin) {
        try {
            _logger.LogInformation("Requesting permission for origin: {Origin}", origin);

            var isGranted = false;
            var ptt = new PermissionsType {
                Origins = [new MatchPattern(new MatchPatternRestricted(origin))]
            };
            if (!isGranted) {
                // prompt via browser UI for permission
                isGranted = await WebExtensions.Permissions.Request(ptt);
            }
            _logger.LogInformation("Permission request result for {Origin}: {IsGranted}", origin, isGranted);
            return isGranted;
        }
        catch (Microsoft.JSInterop.JSException e) {
            _logger.LogError("BW: ... . {e}{s}", e.Message, e.StackTrace);
            throw;
        }
        catch (System.Runtime.InteropServices.JavaScript.JSException e) {
            _logger.LogError("BW: ... 2 . {e}{s}", e.Message, e.StackTrace);
            throw;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error requesting origin permission for {Origin}", origin);
            return false;
        }
    }

    private async Task UseActionPopupAsync(int tabId) {
        try {
            _logger.LogInformation("Using action popup for tab: {TabId}", tabId);

            // Set the popup for this specific tab to show the extension interface
            await WebExtensions.Action.SetPopup(new() {
                Popup = "index.html?environment=popup"
                // Note: TabId may not be supported in SetPopupDetails - this will set for all tabs
            });

            _logger.LogInformation("Action popup configured for tab: {TabId}", tabId);
            // Programmatically open the popup by simulating a click on the action button
            WebExtensions.Action.OpenPopup();
            _logger.LogInformation("Action popup opened for tab: {TabId}", tabId);
            // clear the popup after use, so future clicks trigger the OnActionClicked handler again
            await WebExtensions.Action.SetPopup(new() { Popup = "" });
            _logger.LogInformation("Action popup cleared for future clicks on tab: {TabId}", tabId);
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

    /// <summary>
    /// Serializes an object to JSON and URL-encodes it
    /// </summary>
    private static string SerializeAndEncode(object obj) {
        var jsonString = JsonSerializer.Serialize(obj);
        var encodedString = Uri.EscapeDataString(jsonString);
        return encodedString;
    }

    /// <summary>
    /// Opens an action popup for the specified tab with query parameters
    /// </summary>
    private async Task UseActionPopupAsync(int tabId, List<QueryParam> queryParams) {
        try {
            _logger.LogInformation("BW UseActionPopup acting on tab {TabId}", tabId);

            // Add environment parameter
            queryParams.Add(new QueryParam("environment", "ActionPopup"));

            // Build URL with encoded query strings
            var url = CreateUrlWithEncodedQueryStrings("./index.html", queryParams);

            // Set popup URL (note: SetPopup applies globally, not per-tab in Manifest V3)
            await WebExtensions.Action.SetPopup(new() {
                Popup = url
            });

            // Open the popup
            try {
                WebExtensions.Action.OpenPopup();
                _logger.LogInformation("BW UseActionPopup succeeded");
            }
            catch (Exception ex) {
                // Note: openPopup() sometimes throws even when successful
                _logger.LogDebug(ex, "BW UseActionPopup openPopup() exception (may be expected)");
            }

            // Clear the popup so future clicks trigger OnActionClicked handler
            await WebExtensions.Action.SetPopup(new() { Popup = "" });
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error in UseActionPopup for tab {TabId}", tabId);
        }
    }

    /// <summary>
    /// Creates a URL with encoded query string parameters
    /// </summary>
    private string CreateUrlWithEncodedQueryStrings(string baseUrl, List<QueryParam> queryParams) {
        var fullUrl = _webExtensionsApi.Runtime.GetURL(baseUrl);
        var uriBuilder = new UriBuilder(fullUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);

        foreach (var param in queryParams) {
            if (IsValidKey(param.Key)) {
                // Parameters are already URL-encoded by the caller where needed
                query[param.Key] = param.Value;
            }
            else {
                _logger.LogWarning("BW Invalid key skipped: {Key}", param.Key);
            }
        }

        uriBuilder.Query = query.ToString();
        return uriBuilder.ToString();
    }

    /// <summary>
    /// Validates that a query parameter key contains only safe characters
    /// </summary>
    private static bool IsValidKey(string key) {
        // Allow alphanumeric characters, hyphens, and underscores
        return System.Text.RegularExpressions.Regex.IsMatch(key, @"^[a-zA-Z0-9\-_]+$");
    }

    private string GetAuthorityFromUrl(string url) {
        try {
            if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
                return uri.Authority;
            }
        }
        catch (Exception ex) {
            _logger.LogInformation(ex, "Error extracting authority from URL: {Url}", url);
        }

        return "unknown";
    }

    /// <summary>
    /// Helper record for query parameters
    /// </summary>
    private sealed record QueryParam(string Key, string Value);

    // NOTE: CsConnection and BlazorAppConnection classes removed
    // No longer needed with stateless runtime.sendMessage approach

    private sealed class UpdateDetails {
        public string Reason { get; set; } = "";
        public string PreviousVersion { get; set; } = "";
        public string CurrentVersion { get; set; } = "";
        public string Timestamp { get; set; } = "";
    }

    /// <summary>
    /// Sends a message to a ContentScript in a specific tab using runtime.sendMessage.
    /// </summary>
    /// <param name="tabId">The tab ID to send the message to</param>
    /// <param name="message">The message to send</param>
    private async Task SendMessageToTabAsync(int tabId, object message) {
        try {
            _logger.LogInformation("BW→CS (tab {TabId}): {Message}", tabId, JsonSerializer.Serialize(message));
            await WebExtensions.Tabs.SendMessage(tabId, message);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error sending message to tab {TabId}", tabId);
        }
    }

    /// <summary>
    /// Validates that a message sender is from an allowed origin.
    /// SECURITY: This prevents subdomain attacks - only messages from the extension itself
    /// or from origins with explicit permission grants are allowed.
    ///
    /// SECURITY CHECKS:
    /// 1. Sender must be from this extension (sender.id === chrome.runtime.id)
    /// 2. Sender must have a valid URL
    /// 3. Sender's origin must have explicit permission grant (prevents subdomain attacks)
    /// 4. documentId is checked but not required (Chrome 106+, may not be available in all contexts)
    /// 5. Message payload must be an object with a type field
    /// </summary>
    /// <param name="sender">The message sender</param>
    /// <param name="messageObj">The message object for payload validation</param>
    /// <returns>True if sender and message are valid, false otherwise</returns>
    private async Task<bool> ValidateMessageSenderAsync(WebExtensions.Net.Runtime.MessageSender? sender, object messageObj) {
        try {
            // 1. Check that sender is from this extension
            if (sender?.Id != WebExtensions.Runtime.Id) {
                _logger.LogWarning(
                    "ValidateMessageSender: Message from different extension ID. Expected: {Expected}, Actual: {Actual}",
                    WebExtensions.Runtime.Id,
                    sender?.Id ?? "null"
                );
                return false;
            }

            // 2. Check that sender has a URL
            if (string.IsNullOrEmpty(sender.Url)) {
                _logger.LogWarning("ValidateMessageSender: Message sender has no URL");
                return false;
            }

            // 3. Validate origin has explicit permission grant
            // SECURITY: This prevents subdomain attacks. If permission was granted for
            // https://example.com/*, then https://evil.example.com/* will NOT pass this check.
            string senderOrigin;
            try {
                var url = new Uri(sender.Url);
                senderOrigin = url.GetLeftPart(UriPartial.Authority);
            }
            catch (UriFormatException) {
                _logger.LogWarning("ValidateMessageSender: Invalid sender URL: {Url}", sender.Url);
                return false;
            }

            // Extension pages (popup, tab, sidepanel) are always allowed
            var extensionOrigin = $"chrome-extension://{WebExtensions.Runtime.Id}";
            if (senderOrigin.Equals(extensionOrigin, StringComparison.OrdinalIgnoreCase)) {
                // Extension's own pages are trusted
                _logger.LogDebug("ValidateMessageSender: Message from extension page (trusted)");
                return await ValidatePayloadAsync(messageObj);
            }

            // For content scripts on web pages: check if origin has explicit permission
            // Match pattern for the origin (e.g., "https://example.com/*")
            var matchPattern = $"{senderOrigin}/*";

            var anyPermissions = new WebExtensions.Net.Permissions.AnyPermissions {
                Origins = [new WebExtensions.Net.Manifest.MatchPattern(new WebExtensions.Net.Manifest.MatchPatternRestricted(matchPattern))]
            };

            var hasPermission = await WebExtensions.Permissions.Contains(anyPermissions);

            if (!hasPermission) {
                _logger.LogWarning(
                    "ValidateMessageSender: Sender origin not in granted permissions. Origin: {Origin}, Pattern: {Pattern}. " +
                    "This prevents subdomain attacks - only explicitly granted origins can send messages.",
                    senderOrigin,
                    matchPattern
                );
                return false;
            }

            // 4. na Check for documentId (Chrome 106+, recommended but not required)
            // Note: documentId may not be present in all contexts (e.g., messages from service worker)
            // We already validated sender via extension ID, URL, and explicit permissions
            _logger.LogDebug("ValidateMessageSender: Proceeding with validation. Origin: {Origin}", senderOrigin);

            // 5. Validate payload
            var payloadValid = await ValidatePayloadAsync(messageObj);
            if (!payloadValid) {
                return false;
            }

            // All checks passed
            _logger.LogDebug(
                "ValidateMessageSender: Sender validation passed. Origin: {Origin}",
                senderOrigin
            );
            return true;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error validating message sender");
            return false;
        }
    }

    /// <summary>
    /// Validates message payload structure.
    /// Basic validation - message should be an object with a type field.
    /// </summary>
    /// <param name="messageObj">The message payload object</param>
    /// <returns>True if payload is valid, false otherwise</returns>
    private Task<bool> ValidatePayloadAsync(object messageObj) {
        // Basic validation: message should be an object
        if (messageObj == null) {
            _logger.LogWarning("ValidateMessageSender: Invalid message payload (null)");
            return Task.FromResult(false);
        }

        // Try to serialize and deserialize to check structure
        try {
            var messageJson = JsonSerializer.Serialize(messageObj);
            var messageDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(messageJson);

            if (messageDict == null) {
                _logger.LogWarning("ValidateMessageSender: Invalid message payload (not an object)");
                return Task.FromResult(false);
            }

            // Check for type field
            if (!messageDict.TryGetValue("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String) {
                _logger.LogWarning("ValidateMessageSender: Message payload missing or invalid type field");
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "ValidateMessageSender: Error validating message payload structure");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Handles messages from ContentScript sent via runtime.sendMessage.
    /// These may include polaris-web protocol messages and other messages from CS, and routes them appropriately.
    /// </summary>
    /// <param name="msg">The ContentScript message</param>
    /// <param name="sender">Message sender information</param>
    /// <returns>Response object to send back to ContentScript</returns>
    private async Task HandleContentScriptMessageAsync(CsBwMessage msg, WebExtensions.Net.Runtime.MessageSender? sender) {
        try {
            _logger.LogInformation("BW←CS: {Type}", msg.Type);

            switch (msg.Type) {
                case CsBwMessageTypes.INIT:
                    // ContentScript is initializing - send READY response
                    // TODO P1 send new ReadyMessage();
                    break;

                case CsBwMessageTypes.AUTHORIZE:
                    // Store tab ID for later use
                    var tabId = sender?.Tab?.Id ?? -1;
                    if (tabId == -1) {
                        _logger.LogWarning("BW←CS: No tab ID found for authorize request");
                        return;
                    }

                    // Open popup for user to select and authorize
                    await HandleSelectAuthorizeAsync(msg, sender);
                    // Note: Response will be sent back via separate runtime.sendMessage when user completes action
                    return;

                case CsBwMessageTypes.SELECT_AUTHORIZE_CREDENTIAL:
                    _logger.LogWarning("BW←CS: {Type} not yet implemented", msg.Type);
                    return;

                case CsBwMessageTypes.SELECT_AUTHORIZE_AID:
                    _logger.LogWarning("BW←CS: {Type} not yet implemented", msg.Type);
                    return;

                case CsBwMessageTypes.SIGN_REQUEST:
                    await HandleSignRequestAsync(msg, sender);
                    return;

                case CsBwMessageTypes.SIGNIFY_EXTENSION_CLIENT:
                    // Send the extension ID, although this may be redundantas ContentScript can get it via chrome.runtime.id
                    // TODO P2
                    /*
                    return new {
                        type = BwCsMessageTypes.REPLY,
                        requestId = msg.RequestId,
                        payload = new { extensionId = WebExtensions.Runtime.Id }
                    };
                    */
                    _logger.LogWarning("BW←CS: {Type} not yet implemented", msg.Type);
                    return;

                case CsBwMessageTypes.CREATE_DATA_ATTESTATION:
                    await HandleCreateDataAttestationAsync(msg, sender);
                    return;

                case CsBwMessageTypes.CLEAR_SESSION:
                case CsBwMessageTypes.CONFIGURE_VENDOR:
                case CsBwMessageTypes.GET_CREDENTIAL:
                case CsBwMessageTypes.GET_SESSION_INFO:
                case CsBwMessageTypes.SIGNIFY_EXTENSION:
                case CsBwMessageTypes.SIGN_DATA:
                default:
                    _logger.LogWarning("BW←CS: Unknown message type: {Type}", msg.Type);
                    return;
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling ContentScript message");
            return; // new ErrorReplyMessage(msg.RequestId, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles messages from App (popup/tab/sidepanel).
    /// These messages are typically replies that need to be forwarded to ContentScript.
    /// The App includes the TabId in the message so we know which ContentScript to forward to.
    ///
    /// IMPORTANT: Message type transformation happens here.
    /// App→BW message types (e.g., /KeriAuth/signify/replyCredential) are transformed to
    /// BW→CS message types (e.g., /signify/reply) to maintain separate contracts.
    /// </summary>
    /// <param name="msg">The App message with TabId indicating target tab</param>
    /// <returns>Status response</returns>
    private async Task HandleAppMessageAsync(AppBwMessage msg) {
        try {
            _logger.LogInformation("BW←App: Received message type {Type} from App with tabId {TabId}", msg.Type, msg.TabId);

            if (msg.TabId == null || msg.TabId <= 0) {
                _logger.LogWarning("BW←App: Cannot forward message - no valid tab ID in message");
                return;
            }

            // Transform App message type to ContentScript message type
            // App uses /KeriAuth/signify/replyCredential
            // ContentScript expects /signify/reply
            string contentScriptMessageType;
            switch (msg.Type) {
                case AppBwMessageTypes.REPLY_SIGN:
                case AppBwMessageTypes.REPLY_ERROR:
                case AppBwMessageTypes.REPLY_AID:
                case AppBwMessageTypes.REPLY_CREDENTIAL:
                    contentScriptMessageType = BwCsMessageTypes.REPLY;
                    break;
                case AppBwMessageTypes.REPLY_CANCELED:
                    contentScriptMessageType = BwCsMessageTypes.REPLY_CANCELED;
                    break;
                case AppBwMessageTypes.APP_CLOSED:
                    contentScriptMessageType = BwCsMessageTypes.REPLY_CANCELED; // sic
                    break;
                default:
                    _logger.LogWarning("BW←App: Unknown App message type {Type}, using as-is", msg.Type);
                    contentScriptMessageType = msg.Type;
                    break;
            }

            // Create OutboundMessage for forwarding to ContentScript
            var forwardMsg = new BwCsMessage(
                type: contentScriptMessageType,
                requestId: msg.RequestId,
                payload: msg.Payload,
                error: null
            );

            // Forward the message to the ContentScript on the specified tab
            _logger.LogInformation("BW→CS: Forwarding message type {Type} (transformed from {OriginalType}) to tab {TabId}",
                contentScriptMessageType, msg.Type, msg.TabId);

            var messageJson = JsonSerializer.Serialize(forwardMsg);
            _logger.LogDebug("BW→CS: Message payload: {MessageJson}", messageJson);

            await SendMessageToTabAsync(msg.TabId.Value, forwardMsg);

            _logger.LogInformation("BW→CS: Successfully sent message to tab {TabId}", msg.TabId);

            return;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error forwarding App message to ContentScript");
            return;
        }
    }

    /// <summary>
    /// Handles SELECT_AUTHORIZE message via runtime.sendMessage instead of port
    /// </summary>
    private async Task HandleSelectAuthorizeAsync(CsBwMessage msg, WebExtensions.Net.Runtime.MessageSender? sender) {
        try {
            _logger.LogInformation("BW HandleSelectAuthorize: {Message}", JsonSerializer.Serialize(msg));

            if (sender?.Tab?.Id == null) {
                _logger.LogWarning("BW HandleSelectAuthorize: no tabId found");
                return;
            }

            var tabId = sender.Tab.Id.Value;
            var origin = sender.Url ?? "unknown";
            var jsonOrigin = JsonSerializer.Serialize(origin);

            _logger.LogInformation("BW HandleSelectAuthorize: tabId: {TabId}, origin: {Origin}",
                tabId, jsonOrigin);

            var encodedMsg = SerializeAndEncode(msg);
            await UseActionPopupAsync(tabId, [
                new QueryParam("message", encodedMsg),
                new QueryParam("origin", jsonOrigin),
                new QueryParam("popupType", "SelectAuthorize"),
                new QueryParam("tabId", tabId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            ]);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error in HandleSelectAuthorize");
        }
    }

    /// <summary>
    /// Handles sign request via runtime.sendMessage instead of port
    /// </summary>
    private async Task HandleSignRequestAsync(CsBwMessage msg, WebExtensions.Net.Runtime.MessageSender? sender) {
        try {
            _logger.LogInformation("BW HandleSignRequestAsync: {Message}", JsonSerializer.Serialize(msg));

            if (sender?.Tab?.Id == null) {
                _logger.LogError("BW HandleSignRequestAsync: no tabId found");
                return; // don't have tabId to send error back
            }

            var tabId = sender.Tab.Id.Value;
            var origin = sender.Url ?? "unknown";

            // Extract origin domain from URL
            string originDomain = origin;
            if (Uri.TryCreate(origin, UriKind.Absolute, out var originUri)) {
                originDomain = $"{originUri.Scheme}://{originUri.Host}";
                if (!originUri.IsDefaultPort) {
                    originDomain += $":{originUri.Port}";
                }
            }

            _logger.LogInformation("BW HandleSignRequestAsync: tabId: {TabId}, origin: {Origin}", tabId, originDomain);

            // Deserialize payload to extract method and url
            var payloadJson = JsonSerializer.Serialize(msg.Payload);
            var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson, PortMessageJsonOptions);

            if (payload == null) {
                _logger.LogWarning("BW HandleSignRequestAsync: invalid payload");
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "Invalid payload"));
                return;
            }

            // Extract method and url
            if (!payload.TryGetValue("method", out var methodElement)) {
                _logger.LogWarning("BW HandleSignRequestAsync: method not specified");
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "Method not specified"));
                return;
            }

            if (!payload.TryGetValue("url", out var urlElement)) {
                _logger.LogWarning("BW HandleSignRequestAsync: URL not specified");
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "URL not specified"));
                return;
            }

            var method = methodElement.GetString() ?? "GET";
            var rurl = urlElement.GetString() ?? "";

            if (string.IsNullOrEmpty(rurl)) {
                _logger.LogWarning("BW HandleSignRequestAsync: URL is empty");
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "URL is empty"));
                return;
            }

            // Check if connected to KERIA
            var stateResult = await TryGetSignifyStateAsync();
            if (stateResult.IsFailed) {
                var connectResult = await TryConnectSignifyClientAsync();
                if (connectResult.IsFailed) {
                    _logger.LogWarning("BW HandleSignRequestAsync: failed to connect to KERIA: {Error}", connectResult.Errors[0].Message);
                    await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, $"Not connected to KERIA: {connectResult.Errors[0].Message}"));
                    return;
                }
            }

            // Get AID name for this origin
            string? aidName = null;
            if (Uri.TryCreate(originDomain, UriKind.Absolute, out var websiteOriginUri)) {
                var websiteConfigResult = await _websiteConfigService.GetOrCreateWebsiteConfig(websiteOriginUri);

                if (websiteConfigResult.IsSuccess &&
                    websiteConfigResult.Value.websiteConfig1?.RememberedPrefixOrNothing != null) {

                    var prefix = websiteConfigResult.Value.websiteConfig1.RememberedPrefixOrNothing;
                    var identifiers = await _signifyService.GetIdentifiers();
                    if (identifiers.IsSuccess && identifiers.Value is not null) {
                        var aids = identifiers.Value.Aids;
                        aidName = aids.Where((a) => a.Prefix == prefix).FirstOrDefault()?.Name;
                    }
                }
            }

            if (string.IsNullOrEmpty(aidName)) {
                _logger.LogWarning("BW HandleSignRequestAsync: no identifier configured for origin {Origin}", originDomain);
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "No identifier configured for this origin"));
                return;
            }

            // Get headers from payload
            var headersDict = new Dictionary<string, string>();
            if (payload.TryGetValue("headers", out var headersElement)) {
                try {
                    var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersElement.GetRawText());
                    if (headers != null) {
                        headersDict = headers;
                    }
                }
                catch (Exception ex) {
                    _logger.LogWarning(ex, "BW HandleSignRequestAsync: failed to parse headers");
                }
            }

            // Validate URL is well-formed
            if (!Uri.IsWellFormedUriString(rurl, UriKind.Absolute)) {
                _logger.LogWarning("BW HandleSignRequestAsync: URL is not well-formed: {Url}", rurl);
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "URL is not well-formed"));
                return;
            }

            // TODO P1: Check origin permission previously provided by user in website config, and depending on METHOD (get/post ), and ask user if not granted


            // Get generated signed headers from signify-ts
            var headersDictJson = JsonSerializer.Serialize(headersDict);
            var signedHeadersJson = await _signifyClientBinding.GetSignedHeadersAsync(
                originDomain,
                rurl,
                method,
                headersDictJson,
                aidName
            );

            var signedHeaders = JsonSerializer.Deserialize<Dictionary<string, string>>(signedHeadersJson);
            if (signedHeaders == null) {
                _logger.LogWarning("BW HandleSignRequestAsync: failed to generate signed headers");
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "Failed to generate signed headers"));
                return;
            }

            _logger.LogInformation("BW HandleSignRequestAsync: successfully generated signed headers");
            await SendMessageToTabAsync(tabId, new BwCsMessage(
                type: BwCsMessageTypes.REPLY,
                requestId: msg.RequestId,
                payload: new { headers = signedHeaders },
                error: null
            ));
            return;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error in HandleSignRequestAsync");
            return; // don't have the tabId here to send error back
        }
    }

    /// <summary>
    /// Handles data attestation credential creation request from ContentScript.
    /// Creates a credential based on provided credData and schemaSaid.
    /// </summary>
    private async Task HandleCreateDataAttestationAsync(CsBwMessage msg, WebExtensions.Net.Runtime.MessageSender? sender) {
        try {
            _logger.LogInformation("BW HandleCreateDataAttestation: {Message}", JsonSerializer.Serialize(msg));

            if (sender?.Tab?.Id == null) {
                _logger.LogError("BW HandleCreateDataAttestation: no tabId found");
                return;
            }

            var tabId = sender.Tab.Id.Value;
            var origin = sender.Url ?? "unknown";

            // Extract origin domain from URL
            string originDomain = origin;
            if (Uri.TryCreate(origin, UriKind.Absolute, out var originUri)) {
                originDomain = $"{originUri.Scheme}://{originUri.Host}";
                if (!originUri.IsDefaultPort) {
                    originDomain += $":{originUri.Port}";
                }
            }

            _logger.LogInformation("BW HandleCreateDataAttestation: tabId: {TabId}, origin: {Origin}", tabId, originDomain);

            // Deserialize payload using specific type
            var payloadJson = JsonSerializer.Serialize(msg.Payload);
            var payload = JsonSerializer.Deserialize<CreateDataAttestationPayload>(payloadJson, PortMessageJsonOptions);

            if (payload == null) {
                _logger.LogWarning("BW HandleCreateDataAttestation: invalid payload");
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "Invalid payload"));
                return;
            }

            // Validate required fields
            if (payload.CredData == null || payload.CredData.Count == 0) {
                _logger.LogWarning("BW HandleCreateDataAttestation: credData is empty or missing");
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "Credential data not specified"));
                return;
            }

            if (string.IsNullOrEmpty(payload.SchemaSaid)) {
                _logger.LogWarning("BW HandleCreateDataAttestation: schemaSaid is empty or missing");
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "Schema SAID not specified"));
                return;
            }

            // Convert credData to OrderedDictionary to preserve field order (critical for CESR/SAID)
            var credDataOrdered = new System.Collections.Specialized.OrderedDictionary();
            foreach (var kvp in payload.CredData) {
                credDataOrdered.Add(kvp.Key, kvp.Value);
            }

            // Check if connected to KERIA
            var stateResult = await TryGetSignifyStateAsync();
            if (stateResult.IsFailed) {
                var connectResult = await TryConnectSignifyClientAsync();
                if (connectResult.IsFailed) {
                    _logger.LogWarning("BW HandleCreateDataAttestation: failed to connect to KERIA: {Error}", connectResult.Errors[0].Message);
                    await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, $"Not connected to KERIA: {connectResult.Errors[0].Message}"));
                    return;
                }
            }

            // Get AID name for this origin (using website configuration)
            string? aidName = null;
            if (Uri.TryCreate(originDomain, UriKind.Absolute, out var websiteOriginUri)) {
                var websiteConfigResult = await _websiteConfigService.GetOrCreateWebsiteConfig(websiteOriginUri);

                if (websiteConfigResult.IsSuccess &&
                    websiteConfigResult.Value.websiteConfig1?.RememberedPrefixOrNothing != null) {

                    var prefix = websiteConfigResult.Value.websiteConfig1.RememberedPrefixOrNothing;
                    var identifiers = await _signifyService.GetIdentifiers();
                    if (identifiers.IsSuccess && identifiers.Value is not null) {
                        var aids = identifiers.Value.Aids;
                        aidName = aids.Where((a) => a.Prefix == prefix).FirstOrDefault()?.Name;
                    }
                }
            }

            if (string.IsNullOrEmpty(aidName)) {
                _logger.LogWarning("BW HandleCreateDataAttestation: no identifier configured for origin {Origin}", originDomain);
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "No identifier configured for this origin"));
                return;
            }

            // Get identifier details
            var aidResult = await _signifyService.GetIdentifier(aidName);
            if (aidResult.IsFailed) {
                _logger.LogWarning("BW HandleCreateDataAttestation: failed to get identifier: {Error}", aidResult.Errors[0].Message);
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, $"Failed to get identifier: {aidResult.Errors[0].Message}"));
                return;
            }

            var aid = aidResult.Value;

            // TODO P1: Check if this is a group AID (multisig not currently supported)
            // The TypeScript implementation checks for aid.group property
            // Need to add Group property to Aid model to support this check
            // For now, skipping this validation - multisig credential issuance may fail at KERIA level

            // Get registry for this AID
            var registriesResult = await _signifyService.ListRegistries(aidName);
            if (registriesResult.IsFailed || registriesResult.Value.Count == 0) {
                // TODO P1: Consider automatic registry creation if none exists
                // The TypeScript implementation may create a registry automatically
                // For now, return an error requiring user to create registry first
                _logger.LogWarning("BW HandleCreateDataAttestation: no registry found for AID {AidName}", aidName);
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "No credential registry found for this identifier. Please create a registry first."));
                return;
            }

            var registry = registriesResult.Value[0]; // Use first registry
            var registryId = registry.GetValueOrDefault("regk")?.ToString() ?? "";

            if (string.IsNullOrEmpty(registryId)) {
                _logger.LogWarning("BW HandleCreateDataAttestation: registry ID is empty for AID {AidName}", aidName);
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "Invalid registry configuration"));
                return;
            }

            // Verify schema exists
            var schemaResult = await _signifyService.GetSchema(payload.SchemaSaid);
            if (schemaResult.IsFailed) {
                _logger.LogWarning("BW HandleCreateDataAttestation: failed to get schema {SchemaSaid}: {Error}", payload.SchemaSaid, schemaResult.Errors[0].Message);
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, $"Schema not found: {schemaResult.Errors[0].Message}"));
                return;
            }

            // TODO P1: Consider adding user approval flow before credential creation
            // Depending on security requirements, may want to prompt user similar to HandleSelectAuthorizeAsync
            // This would open a popup for user to review and approve credential creation
            // For now, proceeding with automatic creation for data attestations

            // Build credential data structure
            var credentialData = new CredentialData(
                I: aid.Prefix,              // Issuer prefix
                Ri: registryId,             // Registry ID
                S: payload.SchemaSaid,      // Schema SAID
                A: credDataOrdered          // Credential attributes (order-preserved)
            );

            _logger.LogInformation("BW HandleCreateDataAttestation: issuing credential for AID {AidName} with schema {SchemaSaid}", aidName, payload.SchemaSaid);

            // Issue the credential
            var issueResult = await _signifyService.IssueCredential(aidName, credentialData);
            if (issueResult.IsFailed) {
                _logger.LogWarning("BW HandleCreateDataAttestation: failed to issue credential: {Error}", issueResult.Errors[0].Message);
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, $"Failed to issue credential: {issueResult.Errors[0].Message}"));
                return;
            }

            var credential = issueResult.Value;

            // TODO P1: Add operation waiting if credential issuance returns an operation
            // The TypeScript implementation calls waitOperation() to ensure the credential
            // issuance operation completes before returning to the caller
            // This may require extracting an operation object from the result and waiting for it

            _logger.LogInformation("BW HandleCreateDataAttestation: successfully created credential");

            // Send credential back to ContentScript
            // CRITICAL: Pass RecursiveDictionary directly as payload to preserve CESR/SAID ordering
            // RecursiveDictionary maintains insertion order required for SAID verification
            // The messaging infrastructure will handle serialization properly
            await SendMessageToTabAsync(tabId, new BwCsMessage(
                type: BwCsMessageTypes.REPLY,
                requestId: msg.RequestId,
                payload: credential,
                error: null
            ));
            return;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error in HandleCreateDataAttestation");
            return;
        }
    }

    /// <summary>
    /// Try to get the current SignifyClient state to check if it's connected
    /// </summary>
    private async Task<Result<State>> TryGetSignifyStateAsync() {
        try {
            var state = await _signifyService.GetState();
            return state;
        }
        catch (Exception ex) {
            _logger.LogDebug(ex, "BW TryGetSignifyStateAsync: not connected");
            return Result.Fail<State>("SignifyClient not connected");
        }
    }

    /// <summary>
    /// Try to connect the BackgroundWorker's SignifyClient instance using stored credentials
    /// This is needed because BackgroundWorker has a separate Blazor runtime from the App (popup/tab)
    /// </summary>
    private async Task<Result> TryConnectSignifyClientAsync() {
        try {
            // Get connection config from storage
            var configResult = await _storageService.GetItem<KeriaConnectConfig>();
            if (configResult.IsFailed || configResult.Value == null) {
                return Result.Fail("No KERIA connection configuration found");
            }

            var config = configResult.Value;

            if (string.IsNullOrEmpty(config.AdminUrl)) {
                return Result.Fail("Admin URL not configured");
            }

            if (string.IsNullOrEmpty(config.BootUrl)) {
                return Result.Fail("Boot URL not configured");
            }

            // Get passcode from session storage
            var passcodeResult = await _webExtensionsApi.Storage.Session.Get("passcode");

            // Extract passcode string from the result
            string? passcode = null;
            if (passcodeResult is JsonElement jsonElement) {
                if (jsonElement.ValueKind == JsonValueKind.Undefined || jsonElement.ValueKind == JsonValueKind.Null) {
                    return Result.Fail("Passcode not found in session storage");
                }

                if (jsonElement.TryGetProperty("passcode", out var passcodeProperty)) {
                    passcode = passcodeProperty.GetString();
                }
            }

            if (string.IsNullOrEmpty(passcode)) {
                return Result.Fail("Passcode not available");
            }

            if (passcode.Length != 21) {
                return Result.Fail("Invalid passcode length");
            }

            // Connect to KERIA
            _logger.LogInformation("BW TryConnectSignifyClient: connecting to {AdminUrl}", config.AdminUrl);
            var connectResult = await _signifyService.Connect(
                config.AdminUrl,
                passcode,
                config.BootUrl,
                isBootForced: false  // Don't force boot - just connect
            );

            if (connectResult.IsFailed) {
                return Result.Fail($"Failed to connect: {connectResult.Errors[0].Message}");
            }

            _logger.LogInformation("BW TryConnectSignifyClient: connected successfully");
            return Result.Ok();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "BW TryConnectSignifyClient: exception");
            return Result.Fail($"Exception connecting to KERIA: {ex.Message}");
        }
    }

    public void Dispose() {
        // No resources to dispose - BackgroundWorker uses only injected services
        GC.SuppressFinalize(this);
    }
}
