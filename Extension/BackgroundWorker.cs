using System.Text.Json;
using Blazor.BrowserExtension;
using Extension.Helper;
using Extension.Models;
using Extension.Models.Messages.AppBw;
using Extension.Models.Messages.AppBw.Requests;
using Extension.Models.Messages.AppBw.Responses;
using Extension.Models.Messages.BwApp;
using Extension.Models.Messages.BwApp.Requests;
using Extension.Models.Messages.Common;
using Extension.Models.Messages.CsBw;
using Extension.Models.Messages.ExCs;
using Extension.Models.Messages.Polaris;
using Extension.Models.Messages.Port;
using Extension.Models.Storage;
using Extension.Services;
using Extension.Services.JsBindings;
using Extension.Services.Port;
using Extension.Services.SignifyService;
using Extension.Services.SignifyService.Models;
using Extension.Services.Storage;
using FluentResults;
using JsBind.Net;
using Microsoft.JSInterop;
using WebExtensions.Net;
using WebExtensions.Net.Manifest;
using WebExtensions.Net.Permissions;
using WebExtensions.Net.Runtime;
using AppBwAuthorizeResult = Extension.Models.Messages.AppBw.AuthorizeResult;
using BrowserAlarm = WebExtensions.Net.Alarms.Alarm;
using BrowserTab = WebExtensions.Net.Tabs.Tab;
using MenusContextType = WebExtensions.Net.Menus.ContextType;
using MenusOnClickData = WebExtensions.Net.Menus.OnClickData;
using RemoveInfo = WebExtensions.Net.Tabs.RemoveInfo;
// using System.Xml;

namespace Extension;

/// <summary>
/// Background worker for the browser extension, handling message routing between
/// content scripts, the Blazor app, and KERIA services.
/// </summary>

public partial class BackgroundWorker : BackgroundWorkerBase, IDisposable {

    // Constants
    // TODO P2 move to AppConfig.cs and set a real URL that Chrome will open when the extension is uninstalled, to be used for survey or cleanup instructions.
    private const string UninstallUrl = "https://keriauth.com/uninstall.html";
    private const string DefaultVersion = "unknown";

    // private static bool isInitialized;

    // Message types
    private const string LockAppAction = "LockApp";
    private const string SystemLockDetectedAction = "systemLockDetected";

    // services
    private readonly ILogger<BackgroundWorker> logger;
    private readonly IJSRuntime _jsRuntime;
    private readonly IJsRuntimeAdapter _jsRuntimeAdapter;
    private readonly IStorageService _storageService;
    private readonly ISignifyClientService _signifyClientService;
    private readonly IWebsiteConfigService _websiteConfigService;
    private readonly IPendingBwAppRequestService _pendingBwAppRequestService;
    private readonly IDemo1Binding _demo1Binding;
    private readonly ISchemaService _schemaService;
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
        WebExtensions.Runtime.OnMessage.AddListener(HandleRuntimeOnMessageAsync);
        WebExtensions.Runtime.OnConnect.AddListener(OnConnectAsync);
        WebExtensions.Alarms.OnAlarm.AddListener(OnAlarmAsync);
        // Don't add an OnClicked handler here because it would be invoked after the one registered in app.ts, and may result in race conditions.
        // WebExtensions.Action.OnClicked.AddListener(OnActionClickedAsync);
        WebExtensions.Tabs.OnRemoved.AddListener(OnTabRemovedAsync);
        WebExtensions.Runtime.OnSuspend.AddListener(OnSuspendAsync);
        WebExtensions.Runtime.OnSuspendCanceled.AddListener(OnSuspendCanceledAsync);
        WebExtensions.ContextMenus.OnClicked.AddListener(OnContextMenuClickedAsync);
        // TODO P2 WebExtensions.WebNavigation.OnCompleted.AddListener(OnWebNavCompletedAsync);
        //   Parameters: details - Object with tabId, frameId, url, processId, timeStamp
        // TODO P2 WebExtensions.WebRequest.OnCompleted.AddListener(OnWebReqCompletedAsync);
        //   Parameters: details - Object with requestId, url, method, frameId, tabId, type, timeStamp, etc.
        // TODO P3 WebExtensions.Runtime.OnMessageExternal.AddListener(OnMessageExternalAsync);
        //   Parameters: message (any), sender (MessageSender), sendResponse (function)
        // TODO P2 chrome.webRequest.onBeforeRequest // (network/page events)
        //   Parameters: details - Object with requestId, url, method, frameId, tabId, type, timeStamp, requestBody
    }

    /// <summary>
    /// Handles incoming messages from from ContentScript and App.
    /// </summary>
    [JSInvokable]
    public async Task<bool> HandleRuntimeOnMessageAsync(object arg1, MessageSender sender, Action<object> action, bool b) {
        // TODO P2: implement
        // may be useful to wake up the service worker from idle inactive state
        logger.LogWarning("HandleRuntimeOnMessageAsync called bool: {bool} object: {a}", b, arg1);
        // throw new NotImplementedException();
        return true;  // TODO P2 returning true here keeps this channel open, so no other such listeners should! See app.ts
    }

    private readonly ISignifyClientBinding _signifyClientBinding;
    private readonly SessionManager _sessionManager;
    private readonly ChromeSidePanel _chromeSidePanel;
    private readonly IBwPortService _portService;

    public BackgroundWorker(
        ILogger<BackgroundWorker> logger,
        IJSRuntime jsRuntime,
        IJsRuntimeAdapter jsRuntimeAdapter,
        IStorageService storageService,
        ISignifyClientService signifyService,
        ISignifyClientBinding signifyClientBinding,
        IWebsiteConfigService websiteConfigService,
        IPendingBwAppRequestService pendingBwAppRequestService,
        IDemo1Binding demo1Binding,
        ISchemaService schemaService,
        SessionManager sessionManager,
        IBwPortService portService) {
        this.logger = logger;
        _jsRuntime = jsRuntime;
        _signifyClientBinding = signifyClientBinding;
        _demo1Binding = demo1Binding;
        _jsRuntimeAdapter = jsRuntimeAdapter;
        _storageService = storageService;
        _signifyClientService = signifyService;
        _websiteConfigService = websiteConfigService;
        _pendingBwAppRequestService = pendingBwAppRequestService;
        _schemaService = schemaService;
        _webExtensionsApi = new WebExtensionsApi(_jsRuntimeAdapter);
        _sessionManager = sessionManager;
        _chromeSidePanel = new ChromeSidePanel(_jsRuntimeAdapter);
        _portService = portService;

        // Register RPC handlers for port-based messaging
        _portService.RegisterContentScriptRpcHandler(HandleContentScriptRpcAsync);
        _portService.RegisterAppRpcHandler(HandleAppRpcAsync);
        _portService.RegisterEventHandler(HandlePortEventAsync);
    }

    // onInstalled fires when the extension is first installed, updated, or Chrome is updated. Good for setup tasks (e.g., initialize storage, create default rules).
    // Parameter: details - OnInstalledDetails with reason, previousVersion, and id
    [JSInvokable]
    public async Task OnInstalledAsync(OnInstalledEventCallbackDetails details) {
        try {
            logger.LogInformation("OnInstalledAsync: installed/updated: {Reason}", details.Reason);

            var readyRes = await _signifyClientService.TestAsync();
            if (readyRes.IsSuccess) {
                logger.LogInformation("SignifyClientService is ready onInstalled");
            }
            else {
                logger.LogError("OnInstalledAsync: SignifyClientService is NOT ready: {Errors}", string.Join("; ", readyRes.Errors.Select(e => e.Message)));
            }

            await EnsureInitializedAsync();
            _ = UninstallUrl;

            await CreateContextMenuItemsAsync();

            switch (details.Reason) {
                case OnInstalledReason.Install:
                    await OnInstalledInstallAsync();
                    break;
                case OnInstalledReason.Update:
                    await OnInstalledUpdateAsync(details.PreviousVersion ?? DefaultVersion);
                    break;
                case OnInstalledReason.BrowserUpdate:
                default:
                    // TODO P2 more carefully handle these other Installed reasons
                    // BwReadyState already set by EnsureInitializedAsync above
                    logger.LogInformation("OnInstalledAsync: Unhandled install reason: {Reason}", details.Reason);
                    break;
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, "OnInstalledAsync: Error handling onInstalled event");
            throw;
        }
    }

    /// <summary>
    /// Handles incoming port connections from ContentScript and App.
    /// Delegates to IBwPortService for PortSession management.
    /// </summary>
    [JSInvokable]
    public async Task OnConnectAsync(WebExtensions.Net.Runtime.Port port) {
        try {
            logger.LogInformation("OnConnectAsync: Port connected, name={Name}", port.Name);
            await _portService.HandleConnectAsync(port);
        }
        catch (Exception ex) {
            logger.LogError(ex, "OnConnectAsync: Error handling port connection");
        }
    }

    /// <summary>
    /// Ensures BackgroundWorker is initialized when no lifecycle event fires.
    /// This handles the edge case where an extension is disabled and re-enabled
    /// from chrome://extensions/ - neither OnInstalled nor OnStartup fires in that scenario.
    ///
    /// Checks session storage for BwReadyState and sets it if missing or stale.
    /// Safe to call multiple times - will only perform initialization once per SW lifetime.
    /// </summary>
    private async Task EnsureInitializedAsync() {
        try {
            // Check if BwReadyState is already set in session storage
            var result = await _storageService.GetItem<BwReadyState>(StorageArea.Session);

            if (result.IsSuccess && result.Value?.IsInitialized == true) {
                // Already initialized - nothing to do
                logger.LogDebug("EnsureInitializedAsync: BwReadyState already set, skipping initialization");
                return;
            }

            // BwReadyState not set - this service worker needs to initialize
            logger.LogInformation("EnsureInitializedAsync: BwReadyState not found or not initialized - performing initialization");

            // Ensure skeleton storage records exist
            await InitializeStorageDefaultsAsync();

            // Extend session if unlocked (restores session timeout alarm)
            await _sessionManager.ExtendIfUnlockedAsync();

            // Signal to App that BackgroundWorker initialization is complete
            await SetBwReadyStateAsync();

            // Notify ContentScripts to reconnect their ports after service worker restart
            // await _portService.NotifyContentScriptsOfRestartAsync();

            logger.LogInformation("EnsureInitializedAsync: Initialization complete");
        }
        catch (Exception ex) {
            logger.LogError(ex, "EnsureInitializedAsync: Error during initialization - attempting to set BwReadyState anyway");
            // Still try to set BwReadyState so App doesn't timeout
            await SetBwReadyStateAsync();
        }
    }

    // onStartup fires when Chrome launches with a profile (not incognito) that has the extension installed
    // Typical use: Re-initialize or restore state, reconnect services, refresh caches.
    // Parameters: none
    [JSInvokable]
    public async Task OnStartupAsync() {
        try {
            logger.LogInformation("OnStartupAsync event handler called");
            logger.LogInformation("Browser startup detected - reinitializing background worker");

            // Clear BwReadyState first to force re-initialization on browser startup
            // (session storage may have persisted from previous browser session)
            await _storageService.RemoveItem<BwReadyState>(StorageArea.Session);
            await EnsureInitializedAsync();
            logger.LogInformation("Background worker reinitialized on browser startup");
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error handling onStartup event");
            throw;
        }
    }

    // onAlarm fires at a scheduled interval/time.
    [JSInvokable]
    public async Task OnAlarmAsync(BrowserAlarm alarm) {
        try {
            logger.LogInformation("OnAlarmAsync: '{AlarmName}' fired", alarm.Name);
            await EnsureInitializedAsync();
            switch (alarm.Name) {
                case AppConfig.SessionManagerAlarmName:
                    // Delegate to SessionManager
                    await _sessionManager.HandleAlarmAsync(alarm);
                    return;
                default:
                    logger.LogWarning("Unknown alarm name: {AlarmName}", alarm.Name);
                    return;
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error handling alarm");
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
            logger.LogInformation("OnActionClickedAsync event handler called");

            await EnsureInitializedAsync();

            // Validate tab information
            if (tab is null || string.IsNullOrEmpty(tab.Url)) {
                logger.LogWarning("Invalid tab information");
                return;
            }

            logger.LogInformation("Action button clicked on tab: {TabId}, URL: {Url}", tab.Id, tab.Url);
            // NOTE: Actual permission request and script registration is now handled in app.js

            // 1) Compute per-origin match patterns from the clicked tab
            var matchPatterns = BuildMatchPatternsFromTabUrl(tab.Url);
            if (matchPatterns.Count == 0) {
                logger.LogInformation("KERIAuth BW: Unsupported or restricted URL scheme; not registering persistence. URL: {Url}", tab.Url);
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
                    logger.LogInformation("KERIAuth BW: Persistent host permission already granted for {Patterns}", string.Join(", ", matchPatterns));
                }
                else {
                    logger.LogInformation("KERIAuth BW: Persistent host permission not granted for {Patterns}. Will inject for current tab only using activeTab.", string.Join(", ", matchPatterns));
                }
            }
            catch (Exception ex) {
                logger.LogWarning(ex, "KERIAuth BW: Could not check persistent host permissions - will use activeTab");
            }

            // NOTE: Content script injection is now handled in app.js beforeStart() hook
            // This preserves the user gesture required for permission requests
            // The JavaScript handler runs before this C# handler and handles all injection logic
            logger.LogInformation("KERIAuth BW: Content script injection handled by app.js - no action needed in C# handler");
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error handling action click");
        }
    }

    /// <summary>
    /// Build match patterns from the tab's URL, suitable for both:
    /// - chrome.permissions.{contains,request}({ origins: [...] })
    /// - chrome.scripting.registerContentScripts({ matches: [...] })
    ///
    /// Notes:
    /// - Match patterns don'passcodeModelRes include ports; they'll match any port on that host.
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
            logger.LogDebug(ex, "Error parsing tab URL: {TabUrl}", tabUrl);
        }
        return [];
    }

    // Tabs.onRemoved fires when a tab is closed
    [JSInvokable]
    public async Task OnTabRemovedAsync(int tabId, RemoveInfo removeInfo) {
        try {
            logger.LogInformation("OnTabRemovedAsync event handler called");

            await EnsureInitializedAsync();

            logger.LogInformation("Tab removed: {TabId}, WindowId: {WindowId}, WindowClosing: {WindowClosing}",
                tabId, removeInfo?.WindowId, removeInfo?.IsWindowClosing);

            // Clean up port sessions associated with this tab
            await _portService.HandleTabRemovedAsync(tabId);
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error handling tab removal");
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

    // Runtime.onSuspend event fires just before the background worker is unloaded (idle ~30s).
    // Typical use: Save in-memory state, cleanup, flush logs. ... quickly (though you get very little time).
    // Parameters: none
    [JSInvokable]
    public async Task OnSuspendAsync() {
        try {
            await EnsureInitializedAsync();
            // TODO P2 needs implementation
            ;
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error handling onSuspend");
        }
    }

    // Runtime.OnSuspendCanceled event fires if a previously pending unload is canceled (e.g., because a new event kept the worker alive).
    // Parameters: none
    [JSInvokable]
    public async Task OnSuspendCanceledAsync() {
        try {
            await EnsureInitializedAsync();
            // TODO P2 needs implementation
            ;
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error handling onSuspend");
        }
    }

    // onInstall fires when the extension is installed
    private async Task OnInstalledInstallAsync() {
        try {
            // Clear BwReadyState first to force fresh initialization
            await _storageService.RemoveItem<BwReadyState>(StorageArea.Session);

            await EnsureInitializedAsync();

            var installUrl = _webExtensionsApi.Runtime.GetURL(Routes.IndexPaths.InTab);
            var cp = new WebExtensions.Net.Tabs.CreateProperties {
                Url = installUrl
            };
            var res = await WebExtensions.Tabs.Create(cp) ?? throw new AggregateException("could not create tab");
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error handling install");
            throw;
        }
    }

    /// <summary>
    /// Creates skeleton storage records for essential models if they don't already exist.
    /// This ensures AppCache always has records to work with, even on first run.
    ///
    /// Records created:
    /// - Preferences: User preferences with defaults, IsStored = true
    /// - OnboardState: Onboarding state with IsWelcomed = false, IsStored = true
    /// - KeriaConnectConfig: Requires additional user-provided KERIA URLs
    /// </summary>
    private async Task InitializeStorageDefaultsAsync() {
        try {
            logger.LogInformation("InitializeStorageDefaults: Checking and creating skeleton storage records");

            // Check and create Preferences if not exists
            var prefsResult = await _storageService.GetItem<Preferences>();
            if (prefsResult.IsSuccess && prefsResult.Value is not null && prefsResult.Value.IsStored) {
                logger.LogDebug("InitializeStorageDefaults: Preferences already exists");
            }
            else {
                var defaultPrefs = new Preferences { IsStored = true };
                var setResult = await _storageService.SetItem<Preferences>(defaultPrefs);
                if (setResult.IsFailed) {
                    logger.LogError("InitializeStorageDefaults: Failed to create Preferences: {Error}",
                        string.Join("; ", setResult.Errors.Select(e => e.Message)));
                }
                else {
                    logger.LogInformation("InitializeStorageDefaults: Created skeleton Preferences record");
                }
            }

            // Check and create OnboardState if not exists
            var onboardResult = await _storageService.GetItem<OnboardState>();
            if (onboardResult.IsSuccess && onboardResult.Value is not null && onboardResult.Value.IsStored) {
                logger.LogDebug("InitializeStorageDefaults: OnboardState already exists");
            }
            else {
                var defaultOnboard = new OnboardState { IsStored = true, IsWelcomed = false };
                var setResult = await _storageService.SetItem<OnboardState>(defaultOnboard);
                if (setResult.IsFailed) {
                    logger.LogError("InitializeStorageDefaults: Failed to create OnboardState: {Error}",
                        string.Join("; ", setResult.Errors.Select(e => e.Message)));
                }
                else {
                    logger.LogInformation("InitializeStorageDefaults: Created skeleton OnboardState record");
                }
            }

            // Check and create KeriaConnectConfig skeleton if not exists
            // Unlike Preferences and OnboardState, KeriaConnectConfig requires user input (KERIA URLs)
            // so we only create an empty skeleton with IsStored = true. ConfigurePage will update with real values.
            var configResult = await _storageService.GetItem<KeriaConnectConfig>();
            if (configResult.IsSuccess && configResult.Value is not null && configResult.Value.IsStored) {
                logger.LogDebug("InitializeStorageDefaults: KeriaConnectConfig already exists");
            }
            else {
                var defaultConfig = new KeriaConnectConfig(isStored: true);
                var setResult = await _storageService.SetItem<KeriaConnectConfig>(defaultConfig);
                if (setResult.IsFailed) {
                    logger.LogError("InitializeStorageDefaults: Failed to create KeriaConnectConfig: {Error}",
                        string.Join("; ", setResult.Errors.Select(e => e.Message)));
                }
                else {
                    logger.LogInformation("InitializeStorageDefaults: Created skeleton KeriaConnectConfig record");
                }
            }

            logger.LogInformation("InitializeStorageDefaults: Completed");
        }
        catch (Exception ex) {
            logger.LogError(ex, "InitializeStorageDefaults: Error creating skeleton storage records");
            // Don't throw? - allow extension to continue even if defaults fail
        }
    }

    /// <summary>
    /// Sets BwReadyState.IsInitialized = true in session storage.
    /// Called after BackgroundWorker completes all initialization tasks.
    /// App waits for this flag before reading storage to avoid race conditions.
    /// </summary>
    private async Task SetBwReadyStateAsync() {
        try {
            var readyState = new BwReadyState {
                IsInitialized = true,
                InitializedAtUtc = DateTime.UtcNow
            };
            var result = await _storageService.SetItem<BwReadyState>(readyState, StorageArea.Session);
            if (result.IsFailed) {
                logger.LogError("SetBwReadyStateAsync: Failed to set BwReadyState: {Error}",
                    string.Join("; ", result.Errors.Select(e => e.Message)));
            }
            else {
                logger.LogInformation("SetBwReadyStateAsync: BwReadyState.IsInitialized set to true");
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, "SetBwReadyStateAsync: Error setting BwReadyState");
            // Don't throw - allow extension to continue even if this fails
        }
    }

    public async Task OnContextMenuClickedAsync(MenusOnClickData info, BrowserTab tab) {
        try {
            logger.LogInformation("Context menu clicked: {MenuItemId}", info.MenuItemId);
            await EnsureInitializedAsync();

            switch (info.MenuItemId.Value) {
                case "demo1":
                    // Run demo1 via binding (module is statically imported in BackgroundWorker.js)
                    logger.LogInformation("Running demo1...");
                    logger.LogInformation("Invoking fullname: {asdf}", nameof(_demo1Binding.RunDemo1Async));
                    await _demo1Binding.RunDemo1Async();
                    logger.LogInformation("demo1 completed successfully");
                    break;
                case "demo2":
                    // Run demo2 via binding (module is statically imported in BackgroundWorker.js)
                    logger.LogInformation("Running demo2...");
                    logger.LogInformation("Invoking fullname: {asdf}", nameof(_demo1Binding.RunDemo2Async));
                    await _demo1Binding.RunDemo2Async();
                    logger.LogInformation("demo2 completed successfully");
                    break;
                case "demo3":
                    var dashboardUrl = _webExtensionsApi.Runtime.GetURL("DashboardPage.html");
                    await WebExtensions.Tabs.Create(new WebExtensions.Net.Tabs.CreateProperties {
                        Url = dashboardUrl
                    });
                    break;
                default:
                    logger.LogWarning("Unknown menu item clicked: {MenuItemId}", info.MenuItemId);
                    break;
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error handling context menu click");
        }
    }

    private async Task CreateContextMenuItemsAsync() {
        try {
            logger.LogInformation("Creating context menu items");

            await WebExtensions.ContextMenus.RemoveAll();

            // TODO P2: remove this demo item
            WebExtensions.ContextMenus.Create(new() {
                Id = "demo1",

                Title = "Create test data",
                Contexts = [MenusContextType.Action]
            });

            logger.LogInformation("Context menu items created");
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error creating context menu items");
        }
    }

    //

    // onUpdated fires when the extension is already installed, but updated, which may occur automatically from Chrome and ChromeWebStore, without user intervention
    // This will be triggered by a Chrome Web Store push,
    // or, when sideloading in development, by installing an updated release per the manifest or a Refresh in DevTools.
    private async Task OnInstalledUpdateAsync(string previousVersion) {
        try {
            var currentVersion = WebExtensions.Runtime.GetManifest().GetProperty("version").ToString() ?? DefaultVersion;
            logger.LogInformation("Extension updated from {Previous} to {Current}", previousVersion, currentVersion);

            // Clear BwReadyState first to force fresh initialization after update
            await _storageService.RemoveItem<BwReadyState>(StorageArea.Session);

            // TODO P2: EnsureInitializedAsync handles: storage defaults (may need migration), session manager, and BwReadyState

            await EnsureInitializedAsync();

            // Create pending request for App to notify user of update
            var updatePayload = new Models.Messages.BwApp.Requests.RequestNotifyUserOfUpdatePayload(
                Reason: OnInstalledReason.Update.ToString(),
                PreviousVersion: previousVersion,
                CurrentVersion: currentVersion,
                Timestamp: DateTime.UtcNow.ToString("O")
            );
            var requestId = Guid.NewGuid().ToString();
            var pendingRequest = new Models.Storage.PendingBwAppRequest {
                RequestId = requestId,
                Type = Models.Messages.BwApp.BwAppMessageType.Values.RequestNotifyUserOfUpdate,
                Payload = updatePayload,
                CreatedAtUtc = DateTime.UtcNow
            };
            await _pendingBwAppRequestService.AddRequestAsync(pendingRequest);

            var updateUrl = _webExtensionsApi.Runtime.GetURL(Routes.IndexPaths.InTab);
            var cp = new WebExtensions.Net.Tabs.CreateProperties {
                Url = updateUrl
            };
            var res = await WebExtensions.Tabs.Create(cp) ?? throw new AggregateException("could not create tab");
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error handling update");
        }
    }

    private async Task HandleUnknownMessageActionAsync(string? action) {
        logger.LogWarning("HandleUnknownMessageActionAsync: Unknown message action: {Action}", action);
        await Task.CompletedTask;
        return;
    }

    private async Task HandleLockAppMessageAsync() {
        try {
            logger.LogInformation("HandleLockAppMessageAsync called");
            // The InactivityTimerService handles the actual locking logic
            return;
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error handling lock app message");
            return;
        }
    }

    private async Task HandleSystemLockDetectedAsync() {
        try {
            logger.LogInformation("HandleSystemLockDetectedAsync called");
            logger.LogWarning("System lock/suspend/hibernate detected in background worker");

            // Lock the session immediately for security
            await _sessionManager.LockSessionAsync();
            logger.LogInformation("Session locked due to system lock detection");

            // Broadcast lock event to all connected apps via ports
            // Note: Apps will also detect session lock via storage change,
            // but this provides immediate notification
            try {
                await _portService.BroadcastEventToAllAppsAsync(
                    BwAppMessageType.Values.SystemLockDetected,
                    new { reason = "system_lock" });
                logger.LogInformation("Broadcasted SystemLockDetected event to all apps via ports");
            }
            catch (Exception ex) {
                logger.LogWarning(ex, "Could not broadcast SystemLockDetected event (expected if no apps connected)");
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error handling system lock detection");
        }
    }

    private async Task<bool> CheckOriginPermissionAsync(string origin) {
        try {
            logger.LogInformation("Checking permission for origin: {Origin}", origin);

            var anyPermissions = new AnyPermissions {
                Origins = [new MatchPattern(new MatchPatternRestricted(origin))]
            };
            var hasPermission = await WebExtensions.Permissions.Contains(anyPermissions);

            logger.LogInformation("Permission check result for {Origin}: {HasPermission}", origin, hasPermission);
            return hasPermission;
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error checking origin permission for {Origin}", origin);
            return false;
        }
    }
    private async Task CreateExtensionTabAsync() {
        try {
            var tabUrl = _webExtensionsApi.Runtime.GetURL(Routes.IndexPaths.InTab);
            var cp = new WebExtensions.Net.Tabs.CreateProperties {
                Url = tabUrl
                // TODO P2 use the same tab identifier, so we don't get multiple tabs
            };
            var res = await WebExtensions.Tabs.Create(cp) ?? throw new AggregateException("could not create tab");
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error creating extension tab");
        }
    }

    /// <summary>
    /// Store the pending request for App to retrieve, then uses SidePanel if already open, otherwise action popup for the specified tab.
    /// The App will read the pending request from storage and route to the appropriate page.
    /// </summary>
    /// <param name="pendingRequest">The pending BW→App request to store for App retrieval.</param>
    private async Task UseSidePanelOrActionPopupAsync(PendingBwAppRequest pendingRequest) {
        try {
            logger.LogInformation("BW UseActionPopup: type={Type}, requestId={RequestId}, tabId={TabId}",
                pendingRequest.Type, pendingRequest.RequestId, pendingRequest.TabId);

            // Store the pending request for App to retrieve
            var addResult = await _pendingBwAppRequestService.AddRequestAsync(pendingRequest);
            if (addResult.IsFailed) {
                logger.LogError("BW UseActionPopup: Failed to store pending request: {Error}",
                    addResult.Errors.Count > 0 ? addResult.Errors[0].Message : "Unknown error");
                return;
            }

            // Determine if SidePanel is currently open, and if not use Action popup

            var contextFilter = new ContextFilter() { ContextTypes = [ContextType.SIDEPANEL] };
            var contexts = await _webExtensionsApi.Runtime.GetContexts(contextFilter);
            if (!contexts.Any()) {
                logger.LogInformation("BW UseActionPopup: SidePanel context(s) detected, will use SidePanel for request");

                // Note: SetPopup applies globally, not per-tab in Manifest V3
                await WebExtensions.Action.SetPopup(new() {
                    Popup = new(_webExtensionsApi.Runtime.GetURL(Routes.IndexPaths.InPopup))
                });

                // Open popup
                try {
                    WebExtensions.Action.OpenPopup();
                    logger.LogInformation("BW UseActionPopup succeeded");
                }
                catch (Exception ex) {
                    // Note: openPopup() sometimes throws even when successful
                    logger.LogDebug(ex, "BW UseActionPopup openPopup() exception");
                }

                // Clear the Popup setting so future OpenPopup() invocations will be handled without a tab context
                await WebExtensions.Action.SetPopup(new() {
                    Popup = new WebExtensions.Net.ActionNs.Popup("")
                });
            }
            else {
                logger.LogInformation("BW UseActionPopup: Waiting for SidePanel to detect and handle request");
                // SidePanel, if now or soon opened, will read pending request from storage and navigate accordingly
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, "BW UseActionPopup");
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
                logger.LogWarning(
                    "ValidateMessageSender: Message from different extension ID. Expected: {Expected}, Actual: {Actual}",
                    WebExtensions.Runtime.Id,
                    sender?.Id ?? "null"
                );
                return false;
            }

            // 2. Check that sender has a URL
            if (string.IsNullOrEmpty(sender.Url)) {
                logger.LogWarning("ValidateMessageSender: Message sender has no URL");
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
                logger.LogWarning("ValidateMessageSender: Invalid sender URL: {Url}", sender.Url);
                return false;
            }

            // Extension pages (popup, tab, sidepanel) are always allowed
            var extensionOrigin = $"chrome-extension://{WebExtensions.Runtime.Id}";
            if (senderOrigin.Equals(extensionOrigin, StringComparison.OrdinalIgnoreCase)) {
                // Extension's own pages are trusted
                logger.LogDebug("ValidateMessageSender: Message from extension page (trusted)");
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
                logger.LogWarning(
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
            logger.LogDebug("ValidateMessageSender: Proceeding with validation. Origin: {Origin}", senderOrigin);

            // 5. Validate payload
            var payloadValid = await ValidatePayloadAsync(messageObj);
            if (!payloadValid) {
                return false;
            }

            // All checks passed
            logger.LogDebug(
                "ValidateMessageSender: Sender validation passed. Origin: {Origin}",
                senderOrigin
            );
            return true;
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error validating message sender");
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
            logger.LogWarning("ValidateMessageSender: Invalid message payload (null)");
            return Task.FromResult(false);
        }

        // Try to serialize and deserialize to check structure
        try {
            var messageJson = JsonSerializer.Serialize(messageObj);
            var messageDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(messageJson);

            if (messageDict == null) {
                logger.LogWarning("ValidateMessageSender: Invalid message payload (not an object)");
                return Task.FromResult(false);
            }

            // Check for type field
            if (!messageDict.TryGetValue("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String) {
                logger.LogWarning("ValidateMessageSender: Message payload missing or invalid type field");
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }
        catch (Exception ex) {
            logger.LogWarning(ex, "ValidateMessageSender: Error validating message payload structure");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Handles RPC requests from ContentScript via port-based messaging.
    /// Routes the request to the appropriate handler based on the RPC method.
    /// </summary>
    private async Task HandleContentScriptRpcAsync(string portId, PortSession? portSession, RpcRequest request) {
        logger.LogInformation("BW←CS (port RPC): method={Method}, id={Id}, portId={PortId}",
            request.Method, request.Id, portId);

        if (portSession is null) {
            logger.LogWarning("HandleContentScriptRpcAsync: No PortSession for portId={PortId}", portId);
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: "No PortSession found for this port");
            return;
        }

        if (!portSession.TabId.HasValue) {
            logger.LogWarning("HandleContentScriptRpcAsync: No TabId in PortSession for portId={PortId}", portId);
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: "No TabId associated with this port");
            return;
        }

        var tabId = portSession.TabId.Value;
        var tabUrl = portSession.TabUrl;

        // Extract origin from tab URL
        string origin = "unknown";
        if (tabUrl is not null && Uri.TryCreate(tabUrl, UriKind.Absolute, out var originUri)) {
            origin = $"{originUri.Scheme}://{originUri.Host}";
            if (!originUri.IsDefaultPort) {
                origin += $":{originUri.Port}";
            }
        }

        // Route based on method
        try {
            switch (request.Method) {
                case CsBwMessageTypes.AUTHORIZE:
                case CsBwMessageTypes.SELECT_AUTHORIZE_AID:
                case CsBwMessageTypes.SELECT_AUTHORIZE_CREDENTIAL:
                    await HandleSelectAuthorizeRpcAsync(portId, portSession, request, tabId, tabUrl, origin);
                    return;

                case CsBwMessageTypes.SIGN_REQUEST:
                    await HandleSignRequestRpcAsync(portId, portSession, request, tabId, tabUrl, origin);
                    return;

                case CsBwMessageTypes.SIGN_DATA:
                    await HandleSignDataRpcAsync(portId, portSession, request, tabId, tabUrl, origin);
                    return;

                case CsBwMessageTypes.CREATE_DATA_ATTESTATION:
                    await HandleCreateDataAttestationRpcAsync(portId, portSession, request, tabId, tabUrl, origin);
                    return;

                case CsBwMessageTypes.SIGNIFY_EXTENSION:
                case CsBwMessageTypes.SIGNIFY_EXTENSION_CLIENT:
                    // Return extension ID directly
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                        result: new { extensionId = WebExtensions.Runtime.Id });
                    return;

                case CsBwMessageTypes.CLEAR_SESSION:
                case CsBwMessageTypes.GET_SESSION_INFO:
                    // Sessions not implemented - respond with specific error
                    logger.LogWarning("BW←CS (port RPC): sessions not implemented: {Method}", request.Method);
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                        errorMessage: "Sessions not supported");
                    return;

                case CsBwMessageTypes.GET_CREDENTIAL:
                    // Not implemented yet - respond with specific error
                    logger.LogWarning("BW←CS (port RPC): GetCredential not implemented: {Method}", request.Method);
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                        errorMessage: "GetCredential not implemented");
                    return;

                case CsBwMessageTypes.CONFIGURE_VENDOR:
                    // Not implemented - respond with specific error
                    logger.LogWarning("BW←CS (port RPC): ConfigureVendor not implemented: {Method}", request.Method);
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                        errorMessage: "ConfigureVendor not supported");
                    return;

                case CsBwMessageTypes.INIT:
                    // Legacy method - respond with specific error
                    logger.LogWarning("BW←CS (port RPC): Init is legacy/not implemented: {Method}", request.Method);
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                        errorMessage: "Init method is deprecated");
                    return;

                default:
                    logger.LogWarning("BW←CS (port RPC): Unknown method: {Method}", request.Method);
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                        errorMessage: $"Unknown method: {request.Method}");
                    return;
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error handling ContentScript RPC: method={Method}", request.Method);
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles RPC requests from App (popup/tab/sidepanel) via port-based messaging.
    /// These are typically replies to BW requests or App-initiated actions.
    /// </summary>
    private async Task HandleAppRpcAsync(string portId, PortSession? portSession, RpcRequest request) {
        logger.LogInformation("BW←App (port RPC): method={Method}, id={Id}, portId={PortId}",
            request.Method, request.Id, portId);

        try {
            // Parse the RPC params to extract AppBwMessage fields
            // AppPortService sends: { type, requestId, tabId, tabUrl, payload }
            if (request.Params is not JsonElement paramsElement) {
                logger.LogWarning("BW←App (port RPC): Params is not JsonElement");
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: "Invalid params format");
                return;
            }

            // Extract common fields
            var tabId = paramsElement.TryGetProperty("tabId", out var tabIdProp) ? tabIdProp.GetInt32() : 0;
            var tabUrl = paramsElement.TryGetProperty("tabUrl", out var tabUrlProp) ? tabUrlProp.GetString() : null;
            var requestId = paramsElement.TryGetProperty("requestId", out var reqIdProp) ? reqIdProp.GetString() : null;
            var payload = paramsElement.TryGetProperty("payload", out var payloadProp) ? payloadProp : (JsonElement?)null;

            logger.LogInformation("BW←App (port RPC): Parsed params - tabId={TabId}, tabUrl={TabUrl}, requestId={RequestId}",
                tabId, tabUrl, requestId);

            // Extend session on App activity
            await _sessionManager.ExtendIfUnlockedAsync();

            // Route based on method (which is the message type)
            switch (request.Method) {
                case AppBwMessageType.Values.ReplyAid:
                case AppBwMessageType.Values.ReplyCredential:
                    await HandleAppReplyAuthorizeRpcAsync(portId, request, tabId, tabUrl, requestId, payload);
                    return;

                case AppBwMessageType.Values.ReplyApprovedSignHeaders:
                    await HandleAppReplySignHeadersRpcAsync(portId, request, tabId, tabUrl, requestId, payload);
                    return;

                case AppBwMessageType.Values.ReplySignData:
                    await HandleAppReplySignDataRpcAsync(portId, request, tabId, tabUrl, requestId, payload);
                    return;

                case AppBwMessageType.Values.ReplyCreateCredential:
                    await HandleAppReplyCreateCredentialRpcAsync(portId, request, tabId, tabUrl, requestId, payload);
                    return;

                case AppBwMessageType.Values.ReplyCanceled:
                case AppBwMessageType.Values.ReplyError:
                case AppBwMessageType.Values.AppClosed:
                    await HandleAppReplyCanceledRpcAsync(portId, request, tabId, tabUrl, requestId, request.Method);
                    return;

                case AppBwMessageType.Values.UserActivity:
                    // Session already extended above, just acknowledge
                    logger.LogDebug("BW←App (port RPC): USER_ACTIVITY processed");
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id, result: new { success = true });
                    return;

                case AppBwMessageType.Values.RequestAddIdentifier:
                    await HandleAppRequestAddIdentifierRpcAsync(portId, request, tabId, tabUrl, payload);
                    return;

                case AppBwMessageType.Values.ResponseToBwRequest:
                    // Legacy BW→App request/response pattern - not used with port-based messaging
                    logger.LogInformation("BW←App (port RPC): ResponseToBwRequest received for requestId={RequestId} (legacy path)", requestId);
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id, result: new { success = true });
                    return;

                default:
                    logger.LogWarning("BW←App (port RPC): Unknown method: {Method}", request.Method);
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                        errorMessage: $"Unknown method: {request.Method}");
                    return;
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error handling App RPC: method={Method}", request.Method);
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles ReplyAid and ReplyCredential RPC from App.
    /// Transforms to polaris-web format and forwards to ContentScript.
    /// </summary>
    private async Task HandleAppReplyAuthorizeRpcAsync(string portId, RpcRequest request, int tabId, string? tabUrl, string? requestId, JsonElement? payload) {
        logger.LogInformation("HandleAppReplyAuthorizeRpcAsync: tabId={TabId}, requestId={RequestId}", tabId, requestId);

        if (requestId is null) {
            logger.LogWarning("HandleAppReplyAuthorizeRpcAsync: Missing requestId");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: "Missing requestId");
            return;
        }

        // Get pending request to retrieve port routing info
        PendingBwAppRequest? pendingRequest = null;
        {
            var pendingResult = await _pendingBwAppRequestService.GetRequestAsync(requestId);
            if (pendingResult.IsSuccess) {
                pendingRequest = pendingResult.Value;
            }
        }

        // Clear pending request
        await _pendingBwAppRequestService.RemoveRequestAsync(requestId);

        // Transform payload to polaris-web format
        object? transformedPayload = null;
        if (payload.HasValue) {
            var payloadObj = JsonSerializer.Deserialize<object>(payload.Value.GetRawText(), JsonOptions.RecursiveDictionary);
            transformedPayload = TransformToPolariWebAuthorizeResult(payloadObj);
        }

        // Route response to ContentScript via port
        if (pendingRequest?.PortId is not null && pendingRequest.PortSessionId is not null) {
            logger.LogInformation("BW→CS (port): Sending RPC response for authorize, requestId={RequestId}", requestId);
            await _portService.SendRpcResponseAsync(
                pendingRequest.PortId,
                pendingRequest.PortSessionId,
                pendingRequest.RpcRequestId ?? requestId,
                result: transformedPayload);
        }
        else {
            logger.LogWarning("BW→CS: No port info for authorize response, requestId={RequestId}. Response not sent.", requestId);
        }

        // Acknowledge the App RPC
        await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id, result: new { success = true });
    }

    /// <summary>
    /// Handles ReplyApprovedSignHeaders RPC from App.
    /// Signs the headers and forwards the result to ContentScript.
    /// </summary>
    private async Task HandleAppReplySignHeadersRpcAsync(string portId, RpcRequest request, int tabId, string? tabUrl, string? requestId, JsonElement? payload) {
        logger.LogInformation("HandleAppReplySignHeadersRpcAsync: tabId={TabId}, requestId={RequestId}", tabId, requestId);

        if (payload is null || requestId is null || tabUrl is null) {
            logger.LogWarning("HandleAppReplySignHeadersRpcAsync: Missing required fields");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: "Missing required fields");
            return;
        }

        // Get pending request to retrieve port routing info BEFORE clearing
        PendingBwAppRequest? pendingRequest = null;
        {
            var pendingResult = await _pendingBwAppRequestService.GetRequestAsync(requestId);
            if (pendingResult.IsSuccess) {
                pendingRequest = pendingResult.Value;
            }
        }

        // Clear pending request
        await _pendingBwAppRequestService.RemoveRequestAsync(requestId);

        try {
            // Deserialize payload to AppBwReplySignPayload2
            var signPayload = JsonSerializer.Deserialize<AppBwReplySignPayload2>(payload.Value.GetRawText(), JsonOptions.RecursiveDictionary);

            if (signPayload is null) {
                logger.LogWarning("Could not deserialize payload to AppBwReplySignPayload2");
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: "Invalid sign payload");
                return;
            }

            logger.LogInformation("ReplyApprovedSignHeaders: origin={Origin}, url={Url}, method={Method}",
                signPayload.Origin, signPayload.Url, signPayload.Method);

            // Sign and send - this will forward the result to CS
            await SignAndSendRequestHeaders(tabUrl, tabId,
                new AppBwReplySignMessage(tabId, tabUrl, requestId, signPayload.Origin, signPayload.Url, signPayload.Method, signPayload.Headers, signPayload.Prefix),
                pendingRequest?.PortId,
                pendingRequest?.PortSessionId,
                pendingRequest?.RpcRequestId);

            // Acknowledge the App RPC
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id, result: new { success = true });
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error handling sign headers");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles ReplySignData RPC from App.
    /// Forwards the signed data result to ContentScript.
    /// </summary>
    private async Task HandleAppReplySignDataRpcAsync(string portId, RpcRequest request, int tabId, string? tabUrl, string? requestId, JsonElement? payload) {
        logger.LogInformation("HandleAppReplySignDataRpcAsync: tabId={TabId}, requestId={RequestId}", tabId, requestId);

        if (requestId is null) {
            logger.LogWarning("HandleAppReplySignDataRpcAsync: Missing requestId");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: "Missing requestId");
            return;
        }

        // Get pending request to retrieve port routing info
        PendingBwAppRequest? pendingRequest = null;
        {
            var pendingResult = await _pendingBwAppRequestService.GetRequestAsync(requestId);
            if (pendingResult.IsSuccess) {
                pendingRequest = pendingResult.Value;
            }
        }

        // Clear pending request
        await _pendingBwAppRequestService.RemoveRequestAsync(requestId);

        object? transformedPayload = null;
        string? errorStr = null;

        if (payload.HasValue) {
            try {
                var signDataResult = JsonSerializer.Deserialize<SignDataResult>(payload.Value.GetRawText(), JsonOptions.RecursiveDictionary);
                if (signDataResult is not null) {
                    transformedPayload = signDataResult;
                    logger.LogInformation("ReplySignData: aid={Aid}, itemCount={Count}",
                        signDataResult.Aid, signDataResult.Items?.Length ?? 0);
                }
                else {
                    errorStr = "Failed to process sign-data result";
                }
            }
            catch (Exception ex) {
                logger.LogError(ex, "Error deserializing SignDataResult");
                errorStr = "Error processing sign-data result";
            }
        }

        // Route response to ContentScript
        if (pendingRequest?.PortId is not null && pendingRequest.PortSessionId is not null) {
            logger.LogInformation("BW→CS (port): Sending RPC response for sign-data, requestId={RequestId}", requestId);
            await _portService.SendRpcResponseAsync(
                pendingRequest.PortId,
                pendingRequest.PortSessionId,
                pendingRequest.RpcRequestId ?? requestId,
                result: errorStr is null ? transformedPayload : null,
                errorMessage: errorStr);
        }
        else {
            logger.LogWarning("BW→CS: No port info for sign-data response, requestId={RequestId}. Response not sent.", requestId);
        }

        // Acknowledge the App RPC
        await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id, result: new { success = true });
    }

    /// <summary>
    /// Handles ReplyCreateCredential RPC from App.
    /// Issues the credential via signify-ts and forwards the result to ContentScript.
    /// </summary>
    private async Task HandleAppReplyCreateCredentialRpcAsync(string portId, RpcRequest request, int tabId, string? tabUrl, string? requestId, JsonElement? payload) {
        logger.LogInformation("HandleAppReplyCreateCredentialRpcAsync: tabId={TabId}, requestId={RequestId}", tabId, requestId);

        if (requestId is null || payload is null) {
            logger.LogWarning("HandleAppReplyCreateCredentialRpcAsync: Missing required fields");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: "Missing required fields");
            return;
        }

        // Get pending request to retrieve port routing info
        PendingBwAppRequest? pendingRequest = null;
        {
            var pendingResult = await _pendingBwAppRequestService.GetRequestAsync(requestId);
            if (pendingResult.IsSuccess) {
                pendingRequest = pendingResult.Value;
            }
        }

        // Clear pending request
        await _pendingBwAppRequestService.RemoveRequestAsync(requestId);

        // Reconstruct AppBwMessage for HandleCreateCredentialApprovalAsync
        var appMsg = new AppBwMessage<object>(
            type: AppBwMessageType.Values.ReplyCreateCredential,
            tabId: tabId,
            tabUrl: tabUrl,
            requestId: requestId,
            payload: JsonSerializer.Deserialize<object>(payload.Value.GetRawText(), JsonOptions.RecursiveDictionary)
        );

        // Delegate to handler with port info for routing
        await HandleCreateCredentialApprovalAsync(appMsg,
            pendingRequest?.PortId, pendingRequest?.PortSessionId, pendingRequest?.RpcRequestId);

        // Acknowledge the App RPC
        await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id, result: new { success = true });
    }

    /// <summary>
    /// Handles ReplyCanceled, ReplyError, and AppClosed RPC from App.
    /// Forwards cancel/error to ContentScript.
    /// </summary>
    private async Task HandleAppReplyCanceledRpcAsync(string portId, RpcRequest request, int tabId, string? tabUrl, string? requestId, string messageType) {
        logger.LogInformation("HandleAppReplyCanceledRpcAsync: type={Type}, tabId={TabId}, requestId={RequestId}",
            messageType, tabId, requestId);

        string errorStr = messageType switch {
            AppBwMessageType.Values.ReplyError => "An error occurred in the KERI Auth app",
            AppBwMessageType.Values.AppClosed => "The KERI Auth app was closed",
            _ => "User canceled or rejected request"
        };

        // Get and clear pending request
        PendingBwAppRequest? pendingRequest = null;
        if (!string.IsNullOrEmpty(requestId)) {
            var pendingResult = await _pendingBwAppRequestService.GetRequestAsync(requestId);
            if (pendingResult.IsSuccess) {
                pendingRequest = pendingResult.Value;
            }
            await _pendingBwAppRequestService.RemoveRequestAsync(requestId);
        }

        // Route response to ContentScript
        if (pendingRequest?.PortId is not null && pendingRequest.PortSessionId is not null) {
            logger.LogInformation("BW→CS (port): Sending cancel RPC response, requestId={RequestId}", requestId);
            await _portService.SendRpcResponseAsync(
                pendingRequest.PortId,
                pendingRequest.PortSessionId,
                pendingRequest.RpcRequestId ?? requestId ?? "",
                errorMessage: errorStr);
        }
        else {
            logger.LogWarning("BW→CS: No port info for cancel response, requestId={RequestId}. Response not sent.", requestId);
        }

        // Acknowledge the App RPC
        await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id, result: new { success = true });
    }

    /// <summary>
    /// Handles RequestAddIdentifier RPC from App.
    /// Creates a new identifier via signify-ts and returns the result.
    /// </summary>
    private async Task HandleAppRequestAddIdentifierRpcAsync(string portId, RpcRequest request, int tabId, string? tabUrl, JsonElement? payload) {
        logger.LogInformation("HandleAppRequestAddIdentifierRpcAsync");

        try {
            // Reconstruct AppBwMessage for existing handler
            var appMsg = new AppBwMessage<object>(
                type: AppBwMessageType.Values.RequestAddIdentifier,
                tabId: tabId,
                tabUrl: tabUrl,
                requestId: request.Id,
                payload: payload.HasValue
                    ? JsonSerializer.Deserialize<object>(payload.Value.GetRawText(), JsonOptions.RecursiveDictionary)
                    : null
            );

            // Delegate to existing handler
            var result = await HandleRequestAddIdentifierAsync(appMsg);

            // Return result via RPC response
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id, result: result);
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error creating identifier");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles EVENT messages from ContentScript or App (fire-and-forget notifications).
    /// Events don't require a response - they're used for notifications like user activity.
    /// </summary>
    private async Task HandlePortEventAsync(string portId, PortSession? portSession, Models.Messages.Port.EventMessage eventMessage) {
        logger.LogInformation("BW←Port (EVENT): name={Name}, portId={PortId}", eventMessage.Name, portId);

        switch (eventMessage.Name) {
            case AppBwMessageType.Values.UserActivity:
                // Extend session on user activity
                logger.LogDebug("User activity event received, extending session");
                await _sessionManager.ExtendIfUnlockedAsync();
                break;

            case AppBwMessageType.Values.AppClosed:
                // App closed notification - handle cleanup if needed
                logger.LogInformation("App closed event received from portId={PortId}", portId);
                // Port disconnect will handle cleanup
                break;

            default:
                logger.LogDebug("Unhandled event: name={Name}", eventMessage.Name);
                break;
        }
    }

    /// <summary>
    /// Handles authorize RPC request from ContentScript.
    /// Creates a pending request and opens the popup/sidepanel for user interaction.
    /// </summary>
    private async Task HandleSelectAuthorizeRpcAsync(string portId, PortSession portSession, RpcRequest request,
        int tabId, string? tabUrl, string origin) {
        logger.LogInformation("HandleSelectAuthorizeRpcAsync: method={Method}, tabId={TabId}, origin={Origin}",
            request.Method, tabId, origin);

        // Extract typed params
        var rpcParams = request.GetParams<SelectAuthorizeRpcParams>();
        var originalRequestId = rpcParams?.RequestId ?? request.Id;

        // Create the payload with original CS request details for response routing
        var payload = new RequestSelectAuthorizePayload(
            Origin: origin,
            TabId: tabId,
            TabUrl: tabUrl,
            OriginalRequestId: originalRequestId,
            OriginalType: request.Method,
            OriginalPayload: request.Params
        );

        // Create pending request for App - store port info for response routing
        var pendingRequest = new PendingBwAppRequest {
            RequestId = originalRequestId,
            Type = BwAppMessageType.Values.RequestSelectAuthorize,
            Payload = payload,
            CreatedAtUtc = DateTime.UtcNow,
            TabId = tabId,
            TabUrl = tabUrl,
            // Store port routing info for response
            PortId = portId,
            PortSessionId = portSession.PortSessionId.ToString(),
            RpcRequestId = request.Id
        };

        await UseSidePanelOrActionPopupAsync(pendingRequest);
        // Response will be sent when App replies via HandleAppRpcAsync
    }

    /// <summary>
    /// Handles sign-request RPC from ContentScript.
    /// </summary>
    private async Task HandleSignRequestRpcAsync(string portId, PortSession portSession, RpcRequest request,
        int tabId, string? tabUrl, string origin) {
        logger.LogInformation("HandleSignRequestRpcAsync: tabId={TabId}, origin={Origin}", tabId, origin);

        if (tabUrl is null) {
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: "Tab URL is required for sign-request");
            return;
        }

        // Extract typed params
        var rpcParams = request.GetParams<SignRequestRpcParams>();
        var signRequestPayload = rpcParams?.Payload;

        if (signRequestPayload is null) {
            logger.LogWarning("HandleSignRequestRpcAsync: invalid payload");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: "Invalid payload for sign-request");
            return;
        }

        var originalRequestId = rpcParams?.RequestId ?? request.Id;

        var method = signRequestPayload.Method ?? "GET";
        var requestUrl = signRequestPayload.Url;
        var headers = signRequestPayload.Headers;

        if (string.IsNullOrEmpty(requestUrl)) {
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: "URL must not be empty");
            return;
        }

        // Get or create website config for this origin
        var getOrCreateWebsiteRes = await _websiteConfigService.GetOrCreateWebsiteConfig(new Uri(origin));
        if (getOrCreateWebsiteRes.IsFailed) {
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: "Failed to get or create website config");
            return;
        }

        WebsiteConfig websiteConfig = getOrCreateWebsiteRes.Value.websiteConfig1;
        string? rememberedPrefix = websiteConfig.RememberedPrefixOrNothing;

        if (rememberedPrefix is null) {
            var prefsResult = await _storageService.GetItem<Preferences>();
            rememberedPrefix = prefsResult.IsSuccess && prefsResult.Value is not null
                ? prefsResult.Value.SelectedPrefix
                : AppConfig.DefaultPreferences.SelectedPrefix;
        }

        // Create pending request for sign headers
        var signPayload = new RequestSignHeadersPayload(
            Origin: origin,
            Url: requestUrl,
            Method: method,
            Headers: headers ?? new Dictionary<string, string>(),
            TabId: tabId,
            TabUrl: tabUrl,
            OriginalRequestId: originalRequestId,
            OriginalType: CsBwMessageTypes.SIGN_REQUEST,
            RememberedPrefix: rememberedPrefix
        );

        var pendingRequest = new PendingBwAppRequest {
            RequestId = originalRequestId,
            Type = BwAppMessageType.Values.RequestSignHeaders,
            Payload = signPayload,
            CreatedAtUtc = DateTime.UtcNow,
            TabId = tabId,
            TabUrl = tabUrl,
            PortId = portId,
            PortSessionId = portSession.PortSessionId.ToString(),
            RpcRequestId = request.Id
        };

        await UseSidePanelOrActionPopupAsync(pendingRequest);
    }

    /// <summary>
    /// Handles sign-data RPC from ContentScript.
    /// </summary>
    private async Task HandleSignDataRpcAsync(string portId, PortSession portSession, RpcRequest request,
        int tabId, string? tabUrl, string origin) {
        logger.LogInformation("HandleSignDataRpcAsync: tabId={TabId}, origin={Origin}", tabId, origin);

        // Extract typed params
        var rpcParams = request.GetParams<SignDataRpcParams>();
        var signDataPayload = rpcParams?.Payload;
        var originalRequestId = rpcParams?.RequestId ?? request.Id;

        if (signDataPayload is null || signDataPayload.Items is null || signDataPayload.Items.Length == 0) {
            logger.LogWarning("HandleSignDataRpcAsync: invalid payload - no items");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: "Invalid payload for sign-data: items required");
            return;
        }

        var requestSignDataPayload = new RequestSignDataPayload(
            Origin: origin,
            Message: signDataPayload.Message,
            Items: signDataPayload.Items,
            TabId: tabId,
            TabUrl: tabUrl,
            OriginalRequestId: originalRequestId,
            OriginalType: CsBwMessageTypes.SIGN_DATA
        );

        var pendingRequest = new PendingBwAppRequest {
            RequestId = originalRequestId,
            Type = BwAppMessageType.Values.RequestSignData,
            Payload = requestSignDataPayload,
            CreatedAtUtc = DateTime.UtcNow,
            TabId = tabId,
            TabUrl = tabUrl,
            PortId = portId,
            PortSessionId = portSession.PortSessionId.ToString(),
            RpcRequestId = request.Id
        };

        await UseSidePanelOrActionPopupAsync(pendingRequest);
    }

    /// <summary>
    /// Handles create-data-attestation RPC from ContentScript.
    /// </summary>
    private async Task HandleCreateDataAttestationRpcAsync(string portId, PortSession portSession, RpcRequest request,
        int tabId, string? tabUrl, string origin) {
        logger.LogInformation("HandleCreateDataAttestationRpcAsync: tabId={TabId}, origin={Origin}", tabId, origin);

        // Extract typed params
        var rpcParams = request.GetParams<CreateDataAttestationRpcParams>();
        var createPayload = rpcParams?.Payload;
        var originalRequestId = rpcParams?.RequestId ?? request.Id;

        if (createPayload is null) {
            logger.LogWarning("HandleCreateDataAttestationRpcAsync: invalid payload");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: "Invalid payload for create-data-attestation");
            return;
        }

        var requestPayload = new RequestCreateCredentialPayload(
            Origin: origin,
            CredData: createPayload.CredData,
            SchemaSaid: createPayload.SchemaSaid,
            TabId: tabId,
            TabUrl: tabUrl,
            OriginalRequestId: originalRequestId,
            OriginalType: CsBwMessageTypes.CREATE_DATA_ATTESTATION
        );

        var pendingRequest = new PendingBwAppRequest {
            RequestId = originalRequestId,
            Type = BwAppMessageType.Values.RequestCreateCredential,
            Payload = requestPayload,
            CreatedAtUtc = DateTime.UtcNow,
            TabId = tabId,
            TabUrl = tabUrl,
            PortId = portId,
            PortSessionId = portSession.PortSessionId.ToString(),
            RpcRequestId = request.Id
        };

        await UseSidePanelOrActionPopupAsync(pendingRequest);
    }

    /// <summary>
    /// Transforms an internal AuthorizeResult payload (with Aid) to polaris-web AuthorizeResult format (with identifier).
    ///
    /// Internal format (AppBw AuthorizeResult):
    /// { aid: { name, prefix, salty, transferable, state, windexes }, credential: { raw, cesr } }
    ///
    /// Polaris-web format (BwCsAuthorizeResultPayload):
    /// { identifier: { prefix }, credential: { raw, cesr } }
    /// </summary>
    private BwCsAuthorizeResultPayload? TransformToPolariWebAuthorizeResult(object? payload) {
        if (payload is null) {
            logger.LogWarning("TransformToPolariWebAuthorizeResult: payload is null");
            return null;
        }

        try {
            // Deserialize the payload to AppBw AuthorizeResult (internal format with Aid)
            var payloadJson = JsonSerializer.Serialize(payload, JsonOptions.RecursiveDictionary);
            var authorizeResult = JsonSerializer.Deserialize<AppBwAuthorizeResult>(payloadJson, JsonOptions.RecursiveDictionary);

            if (authorizeResult is null) {
                logger.LogWarning("TransformToPolariWebAuthorizeResult: Could not deserialize to AuthorizeResult");
                return null;
            }

            // Transform to polaris-web compliant format
            BwCsAuthorizeResultIdentifier? identifier = null;
            if (authorizeResult.Aid is not null) {
                identifier = new BwCsAuthorizeResultIdentifier(
                    Prefix: authorizeResult.Aid.Prefix,
                    Name: authorizeResult.Aid.Name
                );
            }

            BwCsAuthorizeResultCredential? credential = null;
            if (authorizeResult.Credential is not null) {
                credential = new BwCsAuthorizeResultCredential(
                    Raw: authorizeResult.Credential.Raw,
                    Cesr: authorizeResult.Credential.Cesr
                );
            }

            var result = new BwCsAuthorizeResultPayload(
                Identifier: identifier,
                Credential: credential
            );

            logger.LogInformation("TransformToPolariWebAuthorizeResult: Transformed payload with identifier prefix={Prefix}",
                identifier?.Prefix ?? "null");

            return result;
        }
        catch (Exception ex) {
            logger.LogError(ex, "TransformToPolariWebAuthorizeResult: Error transforming payload");
            return null;
        }
    }

    /// <summary>
    /// Handles RequestAddIdentifier message from App to create a new identifier.
    /// On success, refreshes identifiers cache and updates storage.
    /// </summary>
    /// <returns>AddIdentifierResponse indicating success/failure</returns>
    private async Task<object?> HandleRequestAddIdentifierAsync(AppBwMessage<object> msg) {
        try {
            logger.LogInformation("BW HandleRequestAddIdentifier: Processing request");

            // Deserialize payload to RequestAddIdentifierPayload
            if (msg.Payload is null) {
                logger.LogWarning("BW HandleRequestAddIdentifier: Payload is null");
                return new AddIdentifierResponse(Success: false, Error: "Payload is null");
            }

            var payloadJson = JsonSerializer.Serialize(msg.Payload, JsonOptions.RecursiveDictionary);
            var payload = JsonSerializer.Deserialize<RequestAddIdentifierPayload>(payloadJson, JsonOptions.RecursiveDictionary);

            if (payload is null || string.IsNullOrEmpty(payload.Alias)) {
                logger.LogWarning("BW HandleRequestAddIdentifier: Invalid payload or empty alias");
                return new AddIdentifierResponse(Success: false, Error: "Invalid payload or empty alias");
            }

            logger.LogInformation("BW HandleRequestAddIdentifier: Creating identifier with alias '{Alias}'", payload.Alias);

            // Create the identifier
            var createResult = await _signifyClientService.RunCreateAid(payload.Alias);
            if (createResult.IsFailed || createResult.Value is null) {
                var errorMsg = string.Join("; ", createResult.Errors.Select(e => e.Message));
                logger.LogWarning("BW HandleRequestAddIdentifier: Failed to create identifier: {Errors}", errorMsg);
                return new AddIdentifierResponse(Success: false, Error: $"Failed to create identifier: {errorMsg}");
            }

            // createResult.Value is a JSON string containing the created AID info
            logger.LogInformation("BW HandleRequestAddIdentifier: Successfully created identifier '{Alias}', result: {Result}", payload.Alias, createResult.Value);

            // Refresh identifiers from KERIA
            var identifiersResult = await _signifyClientService.GetIdentifiers();
            if (identifiersResult.IsFailed) {
                var errorMsg = string.Join("; ", identifiersResult.Errors.Select(e => e.Message));
                logger.LogWarning("BW HandleRequestAddIdentifier: Failed to refresh identifiers: {Errors}", errorMsg);
                return new AddIdentifierResponse(Success: false, Error: $"Identifier created but failed to refresh list: {errorMsg}");
            }

            // Update storage with new identifiers list
            var connectionInfoResult = await _storageService.GetItem<KeriaConnectionInfo>(StorageArea.Session);
            if (connectionInfoResult.IsFailed || connectionInfoResult.Value is null) {
                logger.LogWarning("BW HandleRequestAddIdentifier: Failed to get KeriaConnectionInfo from storage");
                return new AddIdentifierResponse(Success: false, Error: "Failed to get connection info from storage");
            }

            var updatedConnectionInfo = connectionInfoResult.Value with {
                IdentifiersList = [identifiersResult.Value]
            };

            var setResult = await _storageService.SetItem<KeriaConnectionInfo>(updatedConnectionInfo, StorageArea.Session);
            if (setResult.IsFailed) {
                var errorMsg = string.Join("; ", setResult.Errors.Select(e => e.Message));
                logger.LogWarning("BW HandleRequestAddIdentifier: Failed to update KeriaConnectionInfo in storage: {Errors}", errorMsg);
                return new AddIdentifierResponse(Success: false, Error: $"Failed to update storage: {errorMsg}");
            }

            logger.LogInformation("BW HandleRequestAddIdentifier: Successfully updated identifiers in storage");
            return new AddIdentifierResponse(Success: true);
        }
        catch (Exception ex) {
            logger.LogError(ex, "BW HandleRequestAddIdentifier: Exception occurred");
            return new AddIdentifierResponse(Success: false, Error: $"Exception: {ex.Message}");
        }
    }

    private async Task SignAndSendRequestHeaders(string tabUrl, int tabId, AppBwReplySignMessage msg,
        string? portId = null, string? portSessionId = null, string? rpcRequestId = null) {
        // Helper to send response via port
        async Task SendResponseAsync(object? result, string? error) {
            if (portId is not null && portSessionId is not null) {
                // Route via port
                await _portService.SendRpcResponseAsync(portId, portSessionId, rpcRequestId ?? msg.RequestId ?? "",
                    result: error is null ? result : null, errorMessage: error);
            }
            else {
                logger.LogWarning("BW→CS: No port info for sign-headers response, requestId={RequestId}. Response not sent.", msg.RequestId);
            }
        }

        try {
            await EnsureInitializedAsync();

            var payload = msg.Payload;
            if (payload is null) {
                logger.LogError("SignAndSendRequestHeaders: could not parse payload");
                await SendResponseAsync(null, $"SignAndSendRequestHeaders: could not parse payload on msg: {msg}");
                return;
            }

            // Extract values from AppBwReplySignPayload2
            var origin = payload.Origin;
            var requestUrl = payload.Url;
            var method = payload.Method;
            var headers = payload.Headers;
            var prefix = payload.Prefix;

            logger.LogInformation("SignAndSendRequestHeaders: origin={origin}, url={url}, method={method}, prefix={prefix}",
                origin, requestUrl, method, prefix);

            // Validate URL is well-formed
            if (string.IsNullOrEmpty(requestUrl) || !Uri.IsWellFormedUriString(requestUrl, UriKind.Absolute)) {
                logger.LogWarning("SignAndSendRequestHeaders: URL is empty or not well-formed: {Url}", requestUrl);
                await SendResponseAsync(null, "URL is empty or not well-formed");
                return;
            }

            // Validate prefix is provided
            if (string.IsNullOrEmpty(prefix)) {
                logger.LogWarning("SignAndSendRequestHeaders: no identifier prefix provided");
                await SendResponseAsync(null, "No identifier configured for signing");
                return;
            }

            // Get generated signed headers from signify-ts
            var headersDictJson = JsonSerializer.Serialize(headers);

            var readyRes = await _signifyClientService.Ready();
            if (readyRes.IsFailed) {
                logger.LogWarning("SignAndSendRequestHeaders: Signify client not ready: {Error}", readyRes.Errors[0].Message);
                await SendResponseAsync(null, $"Signify client not ready: {readyRes.Errors[0].Message}");
                return;
            }

            var signedHeadersJson = await _signifyClientBinding.GetSignedHeadersAsync(
                origin,
                requestUrl,
                method,
                headersDictJson,
                aidName: prefix
            );

            var signedHeaders = JsonSerializer.Deserialize<Dictionary<string, string>>(signedHeadersJson);
            if (signedHeaders == null) {
                logger.LogWarning("SignAndSendRequestHeaders: failed to generate signed headers");
                await SendResponseAsync(null, "Failed to generate signed headers");
                return;
            }

            logger.LogInformation("SignAndSendRequestHeaders: successfully generated signed headers");
            await SendResponseAsync(new { headers = signedHeaders }, null);
            return;
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error in SignAndSendRequestHeaders");
            await SendResponseAsync(null, $"SignAndSendRequestHeaders: exception occurred.");
            return;
        }
    }

    /// <summary>
    /// Handles user approval of credential creation from App.
    /// Issues the credential using signify-ts and sends the result to ContentScript.
    /// </summary>
    private async Task HandleCreateCredentialApprovalAsync(AppBwMessage<object> msg,
        string? portId = null, string? portSessionId = null, string? rpcRequestId = null) {
        // Helper to send response via port
        async Task SendResponseAsync(object? result, string? error) {
            if (portId is not null && portSessionId is not null) {
                await _portService.SendRpcResponseAsync(portId, portSessionId, rpcRequestId ?? msg.RequestId ?? "",
                    result: error is null ? result : null, errorMessage: error);
            }
            else {
                logger.LogWarning("BW→CS: No port info for create-credential response, requestId={RequestId}. Response not sent.", msg.RequestId);
            }
        }

        try {
            logger.LogInformation("BW HandleCreateCredentialApproval: Processing credential creation approval");

            // Deserialize the approval payload
            var payloadJson = JsonSerializer.Serialize(msg.Payload, JsonOptions.RecursiveDictionary);
            var approvalPayload = JsonSerializer.Deserialize<CreateCredentialApprovalPayload>(payloadJson, JsonOptions.RecursiveDictionary);

            if (approvalPayload is null) {
                logger.LogWarning("BW HandleCreateCredentialApproval: Could not deserialize approval payload");
                await SendResponseAsync(null, "Invalid approval payload");
                return;
            }

            var aidName = approvalPayload.AidName;
            var aidPrefix = approvalPayload.AidPrefix;
            var schemaSaid = approvalPayload.SchemaSaid;

            logger.LogInformation("BW HandleCreateCredentialApproval: aidName={AidName}, aidPrefix={AidPrefix}, schemaSaid={SchemaSaid}",
                aidName, aidPrefix, schemaSaid);

            // Get registry for this AID
            var registriesResult = await _signifyClientService.ListRegistries(aidName);
            if (registriesResult.IsFailed || registriesResult.Value.Count == 0) {
                logger.LogWarning("BW HandleCreateCredentialApproval: no registry found for AID {AidName}", aidName);
                await SendResponseAsync(null, "No credential registry found for this identifier. Please create a registry first.");
                return;
            }

            var registry = registriesResult.Value[0]; // Use first registry
            var registryId = registry.Regk;

            if (string.IsNullOrEmpty(registryId)) {
                logger.LogWarning("BW HandleCreateCredentialApproval: registry ID is empty for AID {AidName}", aidName);
                await SendResponseAsync(null, "Invalid registry configuration");
                return;
            }

            // Verify schema exists, and if not, try to load it via OOBI
            var schemaResult = await _signifyClientService.GetSchema(schemaSaid);
            if (schemaResult.IsFailed) {
                logger.LogInformation("BW HandleCreateCredentialApproval: schema {SchemaSaid} not found in KERIA, attempting to load via OOBI",
                    schemaSaid);

                // Try to resolve the schema OOBI from SchemaService manifest first
                var schemaOobiUrls = _schemaService.GetOobiUrls(schemaSaid);
                if (schemaOobiUrls.Length == 0) {
                    // Fall back to constructing URLs from default OOBI hosts
                    logger.LogInformation("BW HandleCreateCredentialApproval: schema {SchemaSaid} not in manifest, trying default hosts",
                        schemaSaid);
                    schemaOobiUrls = _schemaService.DefaultOobiHosts
                        .Select(host => $"{host}/oobi/{schemaSaid}")
                        .ToArray();
                }
                else {
                    var schemaEntry = _schemaService.GetSchema(schemaSaid);
                    logger.LogInformation("BW HandleCreateCredentialApproval: found schema '{SchemaName}' in manifest with {Count} OOBI URLs",
                        schemaEntry?.Name ?? schemaSaid, schemaOobiUrls.Length);
                }

                bool schemaLoaded = false;
                foreach (var schemaOobi in schemaOobiUrls) {
                    try {
                        logger.LogInformation("BW HandleCreateCredentialApproval: Attempting to resolve schema OOBI: {Oobi}", schemaOobi);
                        var resolveResult = await _signifyClientService.ResolveOobi(schemaOobi);
                        if (resolveResult.IsSuccess) {
                            logger.LogInformation("BW HandleCreateCredentialApproval: Successfully loaded schema {SchemaSaid} from {Oobi}",
                                schemaSaid, schemaOobi);
                            schemaLoaded = true;
                            break;
                        }
                        else {
                            logger.LogWarning("BW HandleCreateCredentialApproval: Failed to resolve schema OOBI {Oobi}: {Error}",
                                schemaOobi, resolveResult.Errors[0].Message);
                        }
                    }
                    catch (Exception ex) {
                        logger.LogWarning(ex, "BW HandleCreateCredentialApproval: Exception resolving schema OOBI {Oobi}", schemaOobi);
                    }
                }

                if (!schemaLoaded) {
                    logger.LogWarning("BW HandleCreateCredentialApproval: Could not load schema {SchemaSaid} from any known source, proceeding anyway",
                        schemaSaid);
                }
            }

            // Convert credData to OrderedDictionary to preserve field order (critical for CESR/SAID)
            var credDataOrdered = new System.Collections.Specialized.OrderedDictionary();
            if (approvalPayload.CredData is JsonElement jsonElement) {
                // If it's a JsonElement, enumerate its properties
                if (jsonElement.ValueKind == JsonValueKind.Object) {
                    foreach (var prop in jsonElement.EnumerateObject()) {
                        credDataOrdered.Add(prop.Name, prop.Value.Clone());
                    }
                }
            }
            else if (approvalPayload.CredData is Dictionary<string, object> dictData) {
                foreach (var kvp in dictData) {
                    credDataOrdered.Add(kvp.Key, kvp.Value);
                }
            }
            else {
                // Fallback: serialize and re-deserialize to get dictionary
                var credDataJson = JsonSerializer.Serialize(approvalPayload.CredData, JsonOptions.RecursiveDictionary);
                var credDataDict = JsonSerializer.Deserialize<Dictionary<string, object>>(credDataJson, JsonOptions.RecursiveDictionary);
                if (credDataDict != null) {
                    foreach (var kvp in credDataDict) {
                        credDataOrdered.Add(kvp.Key, kvp.Value);
                    }
                }
            }

            // Build credential data structure
            var credentialData = new CredentialData(
                I: aidPrefix,               // Issuer prefix
                Ri: registryId,             // Registry ID
                S: schemaSaid,              // Schema SAID
                A: credDataOrdered          // Credential attributes (order-preserved)
            );

            logger.LogInformation("BW HandleCreateCredentialApproval: issuing credential for AID {AidName} with schema {SchemaSaid}",
                aidName, schemaSaid);

            // Issue the credential
            var issueResult = await _signifyClientService.IssueCredential(aidName, credentialData);
            if (issueResult.IsFailed) {
                logger.LogWarning("BW HandleCreateCredentialApproval: failed to issue credential: {Error}",
                    issueResult.Errors[0].Message);
                await SendResponseAsync(null, $"Failed to issue credential: {issueResult.Errors[0].Message}");
                return;
            }

            var credential = issueResult.Value;
            logger.LogInformation("BW HandleCreateCredentialApproval: successfully created credential");

            // Transform IssueCredentialResult to polaris-web CreateCredentialResult format
            // Convert Serder.Ked (OrderedDictionary) to RecursiveDictionary for proper serialization
            var createCredentialResult = new {
                acdc = credential.Acdc.Ked,
                iss = credential.Iss.Ked,
                anc = credential.Anc.Ked,
                op = credential.Op
            };

            // Send credential back to ContentScript via port
            logger.LogInformation("BW→CS (port): Sending create-credential response, requestId={RequestId}", msg.RequestId);
            await SendResponseAsync(createCredentialResult, null);
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error in HandleCreateCredentialApproval");
            await SendResponseAsync(null, "Failed to create credential: " + ex.Message);
        }
    }

    /// <summary>
    /// Get the identifier name for a given prefix from cached session storage.
    /// This avoids calling signifyClientService.GetIdentifiers() repeatedly.
    /// </summary>
    private async Task<Result<string>> GetIdentifierNameFromCacheAsync(string prefix) {
        var connectionInfoResult = await _storageService.GetItem<KeriaConnectionInfo>(StorageArea.Session);
        if (connectionInfoResult.IsFailed || connectionInfoResult.Value == null) {
            return Result.Fail<string>("No KERIA connection info found in session storage");
        }

        var identifiersList = connectionInfoResult.Value.IdentifiersList;
        if (identifiersList == null || identifiersList.Count == 0) {
            return Result.Fail<string>("No identifiers found in cached connection info");
        }

        var allAids = identifiersList.SelectMany(i => i.Aids).ToList();
        var aid = allAids.FirstOrDefault(a => a.Prefix == prefix);
        if (aid == null) {
            return Result.Fail<string>($"No identifier found with prefix {prefix}");
        }

        return Result.Ok(aid.Name);
    }

    /// <summary>
    /// Try to connect the BackgroundWorker's SignifyClient instance using stored credentials
    /// This is needed because BackgroundWorker has a separate Blazor runtime from the App (popup/tab)
    /// </summary>
    private async Task<Result> TryConnectSignifyClientAsync() {
        try {
            // Get connection config from storage and minimally validate
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

            // Retrieve passcode from session storage
            var passcodeResult = await _storageService.GetItem<PasscodeModel>(StorageArea.Session);
            if (passcodeResult.IsFailed) {
                return Result.Fail($"Failed to retrieve passcode: {passcodeResult.Errors[0].Message}");
            }
            if (passcodeResult.Value == null) {
                return Result.Fail("Passcode not found in session storage");
            }
            var passcode = passcodeResult.Value.Passcode;
            if (string.IsNullOrEmpty(passcode)) {
                return Result.Fail("Passcode not available");
            }
            if (passcode.Length != 21) {
                return Result.Fail("Invalid passcode length");
            }

            // Connect to KERIA
            logger.LogInformation("BW TryConnectSignifyClient: connecting to {AdminUrl}", config.AdminUrl);
            var connectResult = await _signifyClientService.Connect(
                config.AdminUrl,
                passcode,
                config.BootUrl,
                isBootForced: false  // Don't force boot - just connect
            );
            if (connectResult.IsFailed) {
                return Result.Fail($"Failed to connect: {connectResult.Errors[0].Message}");
            }
            logger.LogInformation("BW TryConnectSignifyClient: connected successfully");
            return Result.Ok();
        }
        catch (Exception ex) {
            logger.LogError(ex, "BW TryConnectSignifyClient: exception");
            return Result.Fail($"Exception connecting to KERIA: {ex.Message}");
        }
    }

    public void Dispose() {
        GC.SuppressFinalize(this);
    }
}

