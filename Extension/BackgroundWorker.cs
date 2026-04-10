using System.Text.Json;
using Blazor.BrowserExtension;
using Extension.Helper;
using Extension.Models;
using Extension.Models.Messages.AppBw;
using Extension.Models.Messages.AppBw.Requests;
using Extension.Models.Messages.AppBw.Responses;
using Extension.Models.Messages.BwApp;
using Extension.Models.Messages.BwApp.Requests;
using Extension.Models.Messages.CsBw;
using Extension.Models.Messages.ExCs;
using Extension.Models.Messages.Port;
using Extension.Models.Storage;
using Extension.Services;
using Extension.Services.JsBindings;
using Extension.Services.NotificationPollingService;
using Extension.Services.Port;
using Extension.Services.ConfigureService;
using Extension.Services.PrimeDataService;
using Extension.Services.SignifyBroker;
using Extension.Services.SignifyService;
using Extension.Services.SignifyService.Models;
using Extension.Services.Storage;
using Extension.Utilities;
using FluentResults;
using JsBind.Net;
using Microsoft.JSInterop;
using WebExtensions.Net;
using WebExtensions.Net.Alarms;
using WebExtensions.Net.Manifest;
using WebExtensions.Net.Permissions;
using WebExtensions.Net.Runtime;
using AppBwAuthorizeResult = Extension.Models.Messages.AppBw.AuthorizeResult;
using BrowserAlarm = WebExtensions.Net.Alarms.Alarm;
using BrowserTab = WebExtensions.Net.Tabs.Tab;
using MenusContextType = WebExtensions.Net.Menus.ContextType;
using MenusOnClickData = WebExtensions.Net.Menus.OnClickData;
using RemoveInfo = WebExtensions.Net.Tabs.RemoveInfo;

namespace Extension;

/// <summary>
/// Background worker for the browser extension, handling message routing between
/// content scripts, the Blazor app, and KERIA services.
/// </summary>

public partial class BackgroundWorker : BackgroundWorkerBase, IDisposable {

    // Constants
    private static readonly string UninstallUrl = AppConfig.UninstallUrl;
    private const string DefaultVersion = "unknown";

    // private static bool isInitialized;

    // Message types
    private const string LockAppAction = "LockApp";
    private const string SystemLockDetectedAction = "systemLockDetected";

    // services
    private readonly ILogger<BackgroundWorker> logger;
    private readonly IJSRuntime _jsRuntime;
    private readonly IJsRuntimeAdapter _jsRuntimeAdapter;
    private readonly IStorageGateway _storageGateway;
    private readonly ISignifyClientService _signifyClientService;
    private readonly ISignifyRequestBroker _broker;
    private readonly IWebsiteConfigService _websiteConfigService;
    private readonly IPendingBwAppRequestService _pendingBwAppRequestService;
    private readonly ISchemaService _schemaService;
    private readonly WebExtensionsApi _webExtensionsApi;

    // Track the "Open in tab" tab ID so we can reuse it (lost on service worker restart, which is fine)
    private int? _optionsTabId;

    // In-memory flag: resets to false on each SW restart (new BackgroundWorker instance).
    // Prevents EnsureInitializedAsync from being fooled by stale BwReadyState in session storage
    // that persists across SW hibernation cycles.
    private bool _initializedThisLifetime;

    // In-memory correlation for connection invite flow: keyed by page's OOBI
    // Stored after App approval, consumed by the confirm handler
    private record PendingConnectionInfo(string AidName, string AidPrefix, string? ResolvedAlias);
    private readonly Dictionary<string, PendingConnectionInfo> _pendingConnections = new();

    // BwReadyState observer - re-establishes BwReadyState when cleared by App
    private IDisposable? _bwReadyStateObserver;

    // SessionStateModel observer - cancels notification polling when session locks
    private IDisposable? _sessionStateObserver;

    // NOTE: No in-memory state tracking needed for runtime.sendMessage approach
    // All state is derived from message sender info or retrieved from persistent storage

    [BackgroundWorkerMain]
    public override void Main() {
        // JavaScript ES modules are loaded by app.ts beforeStart() hook BEFORE Blazor starts
        // The modules are cached in BackgroundWorker's runtime context and available via IJSRuntime
        // Services can import modules using IJSRuntime.InvokeAsync("import", path) - instant from cache

        // The build-generated backgroundWorker.js invokes the following content as js-equivalents
        // These are important to register here because they help wake or keep the service worker alive by ensuring it responds to relevant events.
        WebExtensions.Runtime.OnInstalled.AddListener(OnInstalledAsync);
        WebExtensions.Runtime.OnStartup.AddListener(OnStartupAsync);
        WebExtensions.Runtime.OnConnect.AddListener(OnConnectAsync);
        WebExtensions.Alarms.OnAlarm.AddListener(OnAlarmAsync);
        WebExtensions.Action.OnClicked.AddListener(OnActionClickedAsync);
        WebExtensions.Tabs.OnRemoved.AddListener(OnTabRemovedAsync);
        WebExtensions.Runtime.OnSuspend.AddListener(OnSuspendAsync);
        WebExtensions.Runtime.OnSuspendCanceled.AddListener(OnSuspendCanceledAsync);
        WebExtensions.ContextMenus.OnClicked.AddListener(OnContextMenuClickedAsync);
        WebExtensions.Runtime.OnMessage.AddListener(OnMessageAsync);

        // NOTE: Do NOT add non-listener calls here. The Blazor.BrowserExtension.Analyzer
        // generates JavaScript equivalents for everything in Main(), and arbitrary method
        // calls get emitted as raw JS function calls that don't exist.
        // WASM readiness is signaled by afterStarted() resolving _wasmReady in app.ts.
    }

    private readonly ISignifyClientBinding _signifyClientBinding;
    private readonly SessionManager _sessionManager;
    private readonly ChromeSidePanel _chromeSidePanel;
    private readonly IBwPortService _portService;
    private readonly IPrimeDataService _primeDataService;
    private readonly IConfigureService _configureService;
    private readonly INotificationPollingService _notificationPollingService;
    private readonly INetworkConnectivityService _networkConnectivityService;
    private CancellationTokenSource? _notificationPollingCts;

    // Monotonic counter for session storage writes. AppCache uses this to detect
    // whether it has processed the latest BW write (WaitForStorageSync).
    // Initialized from POSIX milliseconds so the counter is always higher than any
    // value from a previous BW lifetime (Chrome can restart the service worker while
    // session storage and App instances remain active).
    private long _sessionSeq = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private SessionSequence NextSessionSequence() => new SessionSequence { Seq = Interlocked.Increment(ref _sessionSeq) };

    // Tracks a background connect started by unlock so HandleAppRequestConnectRpcAsync can
    // await it instead of starting a redundant (and destructive) second connect.
    private Task<Result>? _pendingConnectTask;

    public BackgroundWorker(
        ILogger<BackgroundWorker> logger,
        IJSRuntime jsRuntime,
        IJsRuntimeAdapter jsRuntimeAdapter,
        IStorageGateway storageGateway,
        ISignifyClientService signifyService,
        ISignifyClientBinding signifyClientBinding,
        IWebsiteConfigService websiteConfigService,
        IPendingBwAppRequestService pendingBwAppRequestService,
        ISchemaService schemaService,
        SessionManager sessionManager,
        IBwPortService portService,
        IPrimeDataService primeDataService,
        IConfigureService configureService,
        ISignifyRequestBroker broker,
        INotificationPollingService notificationPollingService,
        INetworkConnectivityService networkConnectivityService) {
        this.logger = logger;
        _jsRuntime = jsRuntime;
        _signifyClientBinding = signifyClientBinding;
        _jsRuntimeAdapter = jsRuntimeAdapter;
        _storageGateway = storageGateway;
        _signifyClientService = signifyService;
        _broker = broker;
        _websiteConfigService = websiteConfigService;
        _pendingBwAppRequestService = pendingBwAppRequestService;
        _schemaService = schemaService;
        _webExtensionsApi = new WebExtensionsApi(_jsRuntimeAdapter);
        _sessionManager = sessionManager;
        _chromeSidePanel = new ChromeSidePanel(_jsRuntimeAdapter);
        _portService = portService;
        _primeDataService = primeDataService;
        _configureService = configureService;
        _notificationPollingService = notificationPollingService;
        _networkConnectivityService = networkConnectivityService;
        _networkConnectivityService.OnlineStateChanged += OnNetworkOnlineStateChanged;
        _signifyClientService.KeriaReachabilityChanged += OnKeriaReachabilityChanged;
        _notificationPollingService.OnCredentialNotificationsChanged = () => RefreshCachedCredentialsAsync();
        _notificationPollingService.OnSchemasNeeded = async () => {
            await EnsureAllManifestSchemasResolvedAsync("NotificationPolling");
        };

        // Register RPC handlers for port-based messaging
        _portService.RegisterContentScriptRpcHandler(HandleContentScriptRpcAsync);
        _portService.RegisterAppRpcHandler(HandleAppRpcAsync);
        _portService.RegisterEventHandler(HandlePortEventAsync);
    }

    private void OnNetworkOnlineStateChanged(bool isOnline) {
        _ = WriteNetworkStateAsync(isOnline: isOnline);
    }

    private void OnKeriaReachabilityChanged(bool isReachable) {
        _ = WriteNetworkStateAsync(isKeriaReachable: isReachable);
    }

    private async Task WriteNetworkStateAsync(bool? isOnline = null, bool? isKeriaReachable = null) {
        try {
            var currentResult = await _storageGateway.GetItem<NetworkState>(StorageArea.Session);
            var current = currentResult.IsSuccess ? currentResult.Value : null;
            var baseline = current ?? new NetworkState();
            var updated = baseline with {
                IsOnline = isOnline ?? baseline.IsOnline,
                IsKeriaReachable = isKeriaReachable ?? baseline.IsKeriaReachable
            };
            // Skip write if nothing changed
            if (updated == baseline && current is not null) return;
            await _storageGateway.WriteTransaction(StorageArea.Session, tx => {
                tx.SetItem(updated);
                tx.SetItem(NextSessionSequence());
            });
            logger.LogInformation("NetworkState written: IsOnline={IsOnline}, IsKeriaReachable={IsKeriaReachable}", updated.IsOnline, updated.IsKeriaReachable);
        }
        catch (Exception ex) {
            logger.LogWarning(ex, "Failed to write NetworkState to session storage");
        }
    }

    // onInstalled fires when the extension is first installed, updated, or Chrome is updated. Good for setup tasks (e.g., initialize storage, create default rules).
    // Parameter: details - OnInstalledDetails with reason, previousVersion, and id
    [JSInvokable]
    public async Task OnInstalledAsync(OnInstalledEventCallbackDetails details) {
        try {
            logger.LogInformation(nameof(OnInstalledAsync) + ": installed/updated: {Reason}", details.Reason);

            var readyRes = await _broker.EnqueueReadAsync(SignifyOperation.TestAsync, svc => svc.TestAsync());
            if (readyRes.IsSuccess) {
                logger.LogInformation(nameof(OnInstalledAsync) + ": SignifyClientService is ready onInstalled");
            }
            else {
                logger.LogError(nameof(OnInstalledAsync) + ": SignifyClientService is NOT ready: {Errors}", string.Join("; ", readyRes.Errors.Select(e => e.Message)));
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
                    // TODO P3 more carefully handle these other Installed reasons
                    // BwReadyState already set by EnsureInitializedAsync above
                    logger.LogInformation(nameof(OnInstalledAsync) + ": Unhandled install reason: {Reason}", details.Reason);
                    break;
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(OnInstalledAsync) + ": Error handling onInstalled event");
            throw;
        }
    }

    [JSInvokable]
    public async Task OnMessageAsync(object message, MessageSender sender, bool isResponse) {
        try {
            logger.LogInformation(nameof(OnMessageAsync) + ": message={Message}, sender={Sender}, isResponse={IsResponse}", message, sender, isResponse);

            // Parse message to check type
            JsonElement json;
            if (message is JsonElement el) {
                json = el;
            }
            else {
                json = JsonSerializer.SerializeToElement(message);
            }

            // CLIENT_SW_WAKE is handled entirely by the JS listener in app.ts (beforeStart).
            // The JS listener responds immediately via sendResponse, which is sufficient
            // since its presence in the BW context already proves the service worker is awake.

        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(OnMessageAsync) + ": Error handling message");
        }
    }

    /// <summary>
    /// Handles incoming port connections from ContentScript and App.
    /// Delegates to IBwPortService for PortSession management.
    /// </summary>
    [JSInvokable]
    public async Task OnConnectAsync(WebExtensions.Net.Runtime.Port port) {
        try {
            logger.LogInformation(nameof(OnConnectAsync) + ": Port connected, name={Name}", port.Name);
            await _portService.HandleConnectAsync(port);
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(OnConnectAsync) + ": Error handling port connection");
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
            // In-memory guard: resets on each SW restart (new instance).
            // Session storage BwReadyState persists across SW hibernation cycles,
            // so it alone cannot detect that we need to re-initialize after a wake.
            if (_initializedThisLifetime) {
                logger.LogDebug(nameof(EnsureInitializedAsync) + ": Already initialized this lifetime, skipping");
                return;
            }

            // BwReadyState not set - this service worker needs to initialize
            logger.LogInformation(nameof(EnsureInitializedAsync) + ": Re-initializing (_initializedThisLifetime was false)");

            // Ensure skeleton storage records exist
            await InitializeStorageDefaultsAsync();

            // Extend session if unlocked (restores session timeout alarm)
            if (await _sessionManager.ExtendIfUnlockedAsync()) {
                await _storageGateway.SetItem(NextSessionSequence(), StorageArea.Session);
            }

            // If session is unlocked, try to reconnect signify-ts client
            // This handles the case where service worker was restarted but session is still valid
            await TryReconnectSignifyClientIfSessionUnlockedAsync();

            // Start (or re-start) network connectivity listener — reports current navigator.onLine state
            await _networkConnectivityService.StartListeningAsync();

            // Notify any already-running App, which may have lost their port connection if BW was inactive, so they can reconnect
            try {
                await _jsRuntime.InvokeVoidAsync("chrome.runtime.sendMessage",
                    new { t = SendMessageTypes.SwAppWake });
            }
            catch (Exception ex) {
                logger.LogInformation(ex, nameof(EnsureInitializedAsync) + ": Could not broadcast SW_APP_WAKE (expected if no app pages connected)");
            }

            logger.LogInformation(nameof(EnsureInitializedAsync) + ": Completed all initialization tasks");

            // Signal to App that BackgroundWorker initialization is complete
            await SetBwReadyStateAsync();

            // Subscribe to BwReadyState changes to re-establish it if cleared by App
            // (e.g., during config change via ClearSessionForConfigChangeAsync)
            SubscribeToBwReadyStateChanges();

            // Subscribe to SessionStateModel changes to cancel polling when session locks
            SubscribeToSessionStateChanges();

            // Notify ContentScripts to reconnect their ports after service worker restart
            // await _portService.NotifyContentScriptsOfRestartAsync();

            _initializedThisLifetime = true;
            logger.LogInformation(nameof(EnsureInitializedAsync) + ": Initialization complete");
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(EnsureInitializedAsync) + ": Error during initialization - attempting to set BwReadyState anyway");
            _initializedThisLifetime = true; // Prevent retry loops even on failure
            // Still try to set BwReadyState so App doesn't timeout
            await SetBwReadyStateAsync();
        }
    }

    /// <summary>
    /// Attempts to reconnect the signify-ts client if session is unlocked.
    /// Called during BW initialization after service worker restart.
    /// </summary>
    private async Task TryReconnectSignifyClientIfSessionUnlockedAsync() {
        try {
            // Check if session is unlocked (passcode in memory)
            if (string.IsNullOrEmpty(_sessionManager.GetPasscode())) {
                logger.LogDebug(nameof(TryReconnectSignifyClientIfSessionUnlockedAsync) + ": Session not unlocked (passcode not in memory), skipping reconnect");
                return;
            }

            // Session is unlocked - try to reconnect
            logger.LogInformation(nameof(TryReconnectSignifyClientIfSessionUnlockedAsync) + ": Session unlocked, attempting to reconnect signify-ts client");
            var connectResult = await TryConnectSignifyClientAsync();
            if (connectResult.IsSuccess) {
                logger.LogInformation(nameof(TryReconnectSignifyClientIfSessionUnlockedAsync) + ": Successfully reconnected signify-ts client");
            }
            else {
                var errorMsg = connectResult.Errors.Count > 0 ? connectResult.Errors[0].Message : "Unknown error";
                logger.LogWarning(nameof(TryReconnectSignifyClientIfSessionUnlockedAsync) + ": Failed to reconnect: {Error}", errorMsg);
                // Don't throw - App can still function, user may need to manually unlock
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(TryReconnectSignifyClientIfSessionUnlockedAsync) + ": Exception during reconnect attempt");
            // Don't throw - let initialization continue
        }
    }

    // onStartup fires when Chrome launches with a profile (not incognito) that has the extension installed
    // Typical use: Re-initialize or restore state, reconnect services, refresh caches.
    // Parameters: none
    [JSInvokable]
    public async Task OnStartupAsync() {
        try {
            logger.LogInformation(nameof(OnStartupAsync) + ": event handler called");
            logger.LogInformation(nameof(OnStartupAsync) + ": Browser startup detected - reinitializing background worker");

            // Clear BwReadyState first to force re-initialization on browser startup
            // (session storage may have persisted from previous browser session)
            await _storageGateway.RemoveItem<BwReadyState>(StorageArea.Session);
            await EnsureInitializedAsync();
            logger.LogInformation(nameof(OnStartupAsync) + ": Background worker reinitialized on browser startup");
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(OnStartupAsync) + ": Error handling onStartup event");
            throw;
        }
    }

    // onAlarm fires at a scheduled interval/time.
    [JSInvokable]
    public async Task OnAlarmAsync(BrowserAlarm alarm) {
        try {
            logger.LogInformation(nameof(OnAlarmAsync) + ": '{AlarmName}' fired", alarm.Name);
            await EnsureInitializedAsync();
            switch (alarm.Name) {
                case AppConfig.SessionManagerAlarmName:
                    // Delegate to SessionManager to lock the session
                    await _sessionManager.HandleAlarmAsync(alarm);
                    await _storageGateway.SetItem(NextSessionSequence(), StorageArea.Session);
                    // Clean up any pending requests with inactivity message
                    await _portService.CleanupAllPendingRequestsAsync($"{AppConfig.ProductName} locked due to inactivity");
                    return;
                case AppConfig.NotificationPollAlarmName:
                    try {
                        await PollNotificationsThrottledAsync();
                    }
                    catch (Exception ex) {
                        logger.LogWarning(ex, nameof(OnAlarmAsync) + ": Notification poll alarm failed");
                    }
                    return;
                case SessionManager.SessionKeepAliveAlarmName:
                    _sessionManager.HandleKeepAliveAlarm();
                    return;
                default:
                    logger.LogWarning(nameof(OnAlarmAsync) + ": Unknown alarm name: {AlarmName}", alarm.Name);
                    return;
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(OnAlarmAsync) + ": Error handling alarm");
        }
    }

    // NOTE: Action click permission handling is done in app.js at module level (not in beforeStart())
    // to preserve user gesture AND catch cold-start wake events. See the module-level
    // chrome.action.onClicked.addListener in app.ts for the handler that handles permission
    // requests and script registration.
    //
    // This OnActionClickedAsync method is invoked via the generated BackgroundWorker.js
    // fromReference() queueing mechanism, after the module-level handler above.
    // Typical use: Open popup, toggle feature, 
    [JSInvokable]
    public async Task OnActionClickedAsync(BrowserTab tab) {
        try {
            logger.LogInformation(nameof(OnActionClickedAsync) + ": event handler called");

            await EnsureInitializedAsync();

            /*
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
                logger.LogInformation(AppConfig.LogPrefix + " BW: Unsupported or restricted URL scheme; not registering persistence. URL: {Url}", tab.Url);
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
                    logger.LogInformation(AppConfig.LogPrefix + " BW: Persistent host permission already granted for {Patterns}", string.Join(", ", matchPatterns));
                }
                else {
                    logger.LogInformation(AppConfig.LogPrefix + " BW: Persistent host permission not granted for {Patterns}. Will inject for current tab only using activeTab.", string.Join(", ", matchPatterns));
                }
            }
            catch (Exception ex) {
                logger.LogWarning(ex, AppConfig.LogPrefix + " BW: Could not check persistent host permissions - will use activeTab");
            }
            */
            // NOTE: Content script injection is now handled in app.js beforeStart() hook
            // This preserves the user gesture required for permission requests
            // The JavaScript handler runs before this C# handler and handles all injection logic
            logger.LogInformation(AppConfig.LogPrefix + " BW: Content script injection handled by app.js - no action needed in C# handler");
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(OnActionClickedAsync) + ": Error handling action click");
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
            logger.LogDebug(ex, nameof(BuildMatchPatternsFromTabUrl) + ": Error parsing tab URL: {TabUrl}", tabUrl);
        }
        return [];
    }

    // Tabs.onRemoved fires when a tab is closed
    [JSInvokable]
    public async Task OnTabRemovedAsync(int tabId, RemoveInfo removeInfo) {
        try {
            logger.LogInformation(nameof(OnTabRemovedAsync) + ": event handler called");

            await EnsureInitializedAsync();

            logger.LogInformation(nameof(OnTabRemovedAsync) + ": Tab removed: {TabId}, WindowId: {WindowId}, WindowClosing: {WindowClosing}",
                tabId, removeInfo?.WindowId, removeInfo?.IsWindowClosing);

            // Clean up port sessions associated with this tab
            await _portService.HandleTabRemovedAsync(tabId);
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(OnTabRemovedAsync) + ": Error handling tab removal");
        }
    }

    // Runtime.onSuspend event fires just before the background worker is unloaded (idle ~30s).
    // Typical use: Save in-memory state, cleanup, flush logs. ... quickly (though you get very little time).
    // Parameters: none
    [JSInvokable]
    public async Task OnSuspendAsync() {
        try {
            logger.LogWarning(nameof(OnSuspendAsync) + ": Service worker suspending, cleaning up (will reset _initializedThisLifetime)");

            // Cancel active burst — KERIA calls would fail after unload.
            // Do NOT clear the alarm — it should survive SW restarts to wake for periodic polls.
            _notificationPollingCts?.Cancel();
            _notificationPollingCts?.Dispose();
            _notificationPollingCts = null;

            // Clear in-flight connection correlations — stale after wake
            _pendingConnections.Clear();

            // Reset tab tracking — tab may not exist after wake
            _optionsTabId = null;

            // Mark uninitialized so EnsureInitializedAsync runs fresh on wake
            _initializedThisLifetime = false;

            await Task.CompletedTask;
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(OnSuspendAsync) + ": Error handling onSuspend");
        }
    }

    // Runtime.OnSuspendCanceled event fires if a previously pending unload is canceled (e.g., because a new event kept the worker alive).
    // Parameters: none
    [JSInvokable]
    public async Task OnSuspendCanceledAsync() {
        try {
            logger.LogDebug(nameof(OnSuspendCanceledAsync) + ": Suspend canceled, worker staying alive");
            await Task.CompletedTask;
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(OnSuspendCanceledAsync) + ": Error handling onSuspendCanceled");
        }
    }

    // onInstall fires when the extension is installed
    private async Task OnInstalledInstallAsync() {
        try {
            // Clear BwReadyState first to force fresh initialization
            await _storageGateway.RemoveItem<BwReadyState>(StorageArea.Session);

            await EnsureInitializedAsync();

            var installUrl = _webExtensionsApi.Runtime.GetURL(Routes.IndexPaths.InTab);
            var cp = new WebExtensions.Net.Tabs.CreateProperties {
                Url = installUrl
            };
            var newTab = await WebExtensions.Tabs.Create(cp) ?? throw new AggregateException("could not create tab");
            _optionsTabId = newTab.Id;
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(OnInstalledInstallAsync) + ": Error handling install");
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
            logger.LogInformation(nameof(InitializeStorageDefaultsAsync) + ": Checking and creating skeleton storage records");

            // Step 1: Detect schema version mismatches across all versioned Local records.
            // This must happen BEFORE any GetItem<T>() call would discard stale data,
            // so we can record what was discarded for the user-facing migration notice.
            var discardedTypes = new List<string>();
            await ProbeAndRecordMismatchAsync<Preferences>(discardedTypes);
            await ProbeAndRecordMismatchAsync<OnboardState>(discardedTypes);
            await ProbeAndRecordMismatchAsync<KeriaConnectConfigs>(discardedTypes);

            if (discardedTypes.Count > 0) {
                logger.LogWarning(nameof(InitializeStorageDefaultsAsync) +
                    ": Schema version mismatches detected — clearing {Count} stale records: {Types}",
                    discardedTypes.Count, string.Join(", ", discardedTypes));

                // Write the MigrationNotice so the App can warn the user on next page render.
                // The App is a passive displayer — BW is authoritative for detection.
                var notice = new MigrationNotice { DiscardedTypeNames = discardedTypes };
                var noticeResult = await _storageGateway.SetItem(notice);
                if (noticeResult.IsFailed) {
                    logger.LogError(nameof(InitializeStorageDefaultsAsync) +
                        ": Failed to write MigrationNotice: {Error}",
                        string.Join("; ", noticeResult.Errors.Select(e => e.Message)));
                }

                // Remove each stale record so subsequent GetItem<T>() calls see NotFound
                // and the skeleton-creation paths below take over cleanly.
                foreach (var typeName in discardedTypes) {
                    await RemoveStaleRecordAsync(typeName);
                }
            }

            // Step 2: Create skeleton records where missing (or after being cleared above).

            // Check and create Preferences if not exists
            var prefsResult = await _storageGateway.GetItem<Preferences>();
            if (prefsResult.IsSuccess && prefsResult.Value is not null && prefsResult.Value.IsStored) {
                logger.LogDebug(nameof(InitializeStorageDefaultsAsync) + ": Preferences already exists");
            }
            else {
                var defaultPrefs = new Preferences { IsStored = true };
                var setResult = await _storageGateway.SetItem<Preferences>(defaultPrefs);
                if (setResult.IsFailed) {
                    logger.LogError(nameof(InitializeStorageDefaultsAsync) + ": Failed to create Preferences: {Error}",
                        string.Join("; ", setResult.Errors.Select(e => e.Message)));
                }
                else {
                    logger.LogInformation(nameof(InitializeStorageDefaultsAsync) + ": Created skeleton Preferences record");
                }
            }

            // Check and create OnboardState if not exists
            var onboardResult = await _storageGateway.GetItem<OnboardState>();
            if (onboardResult.IsSuccess && onboardResult.Value is not null && onboardResult.Value.IsStored) {
                logger.LogDebug(nameof(InitializeStorageDefaultsAsync) + ": OnboardState already exists");
            }
            else {
                var defaultOnboard = new OnboardState { IsStored = true, IsWelcomed = false };
                var setResult = await _storageGateway.SetItem<OnboardState>(defaultOnboard);
                if (setResult.IsFailed) {
                    logger.LogError(nameof(InitializeStorageDefaultsAsync) + ": Failed to create OnboardState: {Error}",
                        string.Join("; ", setResult.Errors.Select(e => e.Message)));
                }
                else {
                    logger.LogInformation(nameof(InitializeStorageDefaultsAsync) + ": Created skeleton OnboardState record");
                }
            }

            // Check and create KeriaConnectConfigs skeleton if not exists
            // Unlike Preferences and OnboardState, KeriaConnectConfigs requires user input (KERIA URLs)
            // so we only create an empty skeleton with IsStored = true. ConfigurePage will update with real values.
            var configsResult = await _storageGateway.GetItem<KeriaConnectConfigs>();
            if (configsResult.IsSuccess && configsResult.Value is not null && configsResult.Value.IsStored) {
                logger.LogDebug(nameof(InitializeStorageDefaultsAsync) + ": KeriaConnectConfigs already exists (count={Count})", configsResult.Value.Configs.Count);
            }
            else {
                var defaultConfigs = new KeriaConnectConfigs { IsStored = true };
                var setResult = await _storageGateway.SetItem<KeriaConnectConfigs>(defaultConfigs);
                if (setResult.IsFailed) {
                    logger.LogError(nameof(InitializeStorageDefaultsAsync) + ": Failed to create KeriaConnectConfigs: {Error}",
                        string.Join("; ", setResult.Errors.Select(e => e.Message)));
                }
                else {
                    logger.LogInformation(nameof(InitializeStorageDefaultsAsync) + ": Created skeleton KeriaConnectConfigs record");
                }
            }

            logger.LogInformation(nameof(InitializeStorageDefaultsAsync) + ": Completed");
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(InitializeStorageDefaultsAsync) + ": Error creating skeleton storage records");
            // Don't throw? - allow extension to continue even if defaults fail
        }
    }

    /// <summary>
    /// Probes a versioned Local storage record. If a version mismatch is detected,
    /// appends the type name to the discardedTypes list.
    /// </summary>
    private async Task ProbeAndRecordMismatchAsync<T>(List<string> discardedTypes) where T : class, IVersionedStorageModel {
        var statusResult = await _storageGateway.GetItemStatus<T>();
        if (statusResult.IsSuccess && statusResult.Value == StorageItemStatus.VersionMismatch) {
            discardedTypes.Add(typeof(T).Name);
        }
    }

    /// <summary>
    /// Removes a stale Local storage record by type name. Since RemoveItem is generic,
    /// dispatches to the correct type based on the string name.
    /// </summary>
    private async Task RemoveStaleRecordAsync(string typeName) {
        switch (typeName) {
            case nameof(Preferences):
                await _storageGateway.RemoveItem<Preferences>();
                break;
            case nameof(OnboardState):
                await _storageGateway.RemoveItem<OnboardState>();
                break;
            case nameof(KeriaConnectConfigs):
                await _storageGateway.RemoveItem<KeriaConnectConfigs>();
                break;
            default:
                logger.LogWarning(nameof(RemoveStaleRecordAsync) + ": Unknown stale record type {Type} — skipping", typeName);
                break;
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
            var result = await _storageGateway.WriteTransaction(StorageArea.Session, tx => {
                tx.SetItem(readyState);
                tx.SetItem(NextSessionSequence());
            });
            if (result.IsFailed) {
                logger.LogError(nameof(SetBwReadyStateAsync) + ": Failed to set BwReadyState: {Error}",
                    string.Join("; ", result.Errors.Select(e => e.Message)));
            }
            else {
                logger.LogInformation(nameof(SetBwReadyStateAsync) + ": BwReadyState.IsInitialized set to true");
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(SetBwReadyStateAsync) + ": Error setting BwReadyState");
            // Don't throw - allow extension to continue even if this fails
        }
    }

    /// <summary>
    /// Subscribes to BwReadyState changes to re-establish it when cleared.
    /// This handles the case where App clears session storage (e.g., during config change)
    /// but BackgroundWorker is still running and ready to handle requests.
    /// </summary>
    private void SubscribeToBwReadyStateChanges() {
        if (_bwReadyStateObserver != null) {
            logger.LogDebug(nameof(SubscribeToBwReadyStateChanges) + ": Already subscribed");
            return;
        }

        _bwReadyStateObserver = _storageGateway.Subscribe(
            new BwReadyStateObserver(this),
            StorageArea.Session
        );
        logger.LogDebug(nameof(SubscribeToBwReadyStateChanges) + ": Subscribed to BwReadyState changes");
    }

    /// <summary>
    /// Handles BwReadyState being cleared - immediately re-establishes it.
    /// </summary>
    private async Task HandleBwReadyStateClearedAsync() {
        try {
            logger.LogInformation(nameof(HandleBwReadyStateClearedAsync) + ": BwReadyState was cleared, re-establishing...");
            await SetBwReadyStateAsync();
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleBwReadyStateClearedAsync) + ": Failed to re-establish BwReadyState");
        }
    }

    /// <summary>
    /// Observer for BwReadyState changes. Re-establishes BwReadyState when cleared.
    /// </summary>
    private sealed class BwReadyStateObserver(BackgroundWorker backgroundWorker) : IObserver<BwReadyState> {
        public void OnNext(BwReadyState? value) {
            // Only act if BwReadyState was cleared (null or not initialized)
            if (value is null || !value.IsInitialized) {
                backgroundWorker.logger.LogDebug(nameof(BwReadyStateObserver) + ": BwReadyState cleared or not initialized, re-establishing");
                _ = backgroundWorker.HandleBwReadyStateClearedAsync();
            }
            // If value.IsInitialized is true, this was us setting it - no action needed
        }

        public void OnError(Exception error) {
            backgroundWorker.logger.LogError(error, nameof(BwReadyStateObserver) + ": Error observing BwReadyState");
        }

        public void OnCompleted() {
            backgroundWorker.logger.LogDebug(nameof(BwReadyStateObserver) + ": Observer completed");
        }
    }

    /// <summary>
    /// Subscribes to SessionStateModel changes to cancel notification polling when session locks.
    /// When SessionStateModel is cleared (session lock/expiration), polling is stopped to avoid
    /// futile KERIA calls against a disconnected signify client.
    /// </summary>
    private void SubscribeToSessionStateChanges() {
        if (_sessionStateObserver != null) {
            logger.LogDebug(nameof(SubscribeToSessionStateChanges) + ": Already subscribed");
            return;
        }

        _sessionStateObserver = _storageGateway.Subscribe(
            new SessionStatePollingObserver(this),
            StorageArea.Session
        );
        logger.LogDebug(nameof(SubscribeToSessionStateChanges) + ": Subscribed to SessionStateModel changes");
    }

    /// <summary>
    /// Observer for SessionStateModel changes. Cancels notification polling when session locks.
    /// </summary>
    private sealed class SessionStatePollingObserver(BackgroundWorker backgroundWorker) : IObserver<SessionStateModel> {
        public void OnNext(SessionStateModel? value) {
            if (value is null || value.SessionExpirationUtc == DateTime.MinValue) {
                backgroundWorker.logger.LogInformation(nameof(SessionStatePollingObserver) + ": SessionStateModel cleared — disconnecting signify client and canceling notification polling");
                _ = backgroundWorker._broker.EnqueueCommandAsync(SignifyOperation.Disconnect, async svc => { await svc.Disconnect(); return Result.Ok(); });
                _ = backgroundWorker.CancelNotificationPollingAsync();
            }
        }

        public void OnError(Exception error) {
            backgroundWorker.logger.LogError(error, nameof(SessionStatePollingObserver) + ": Error observing SessionStateModel");
        }

        public void OnCompleted() {
            backgroundWorker.logger.LogDebug(nameof(SessionStatePollingObserver) + ": Observer completed");
        }
    }

    public async Task OnContextMenuClickedAsync(MenusOnClickData info, BrowserTab tab) {
        try {
            logger.LogInformation(nameof(OnContextMenuClickedAsync) + ": Context menu clicked: {MenuItemId}", info.MenuItemId);
            await EnsureInitializedAsync();

            switch (info.MenuItemId.Value) {
                case "launchTab":
                    await OpenOrFocusOptionsTabAsync();
                    break;
                case "openSidePanel":
                    // Side panel open is handled by JS listener in app.ts for user gesture preservation
                    break;
                default:
                    logger.LogWarning(nameof(OnContextMenuClickedAsync) + ": Unknown menu item clicked: {MenuItemId}", info.MenuItemId);
                    break;
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(OnContextMenuClickedAsync) + ": Error handling context menu click");
        }
    }

    private async Task CreateContextMenuItemsAsync() {
        try {
            logger.LogDebug(nameof(CreateContextMenuItemsAsync) + ": Creating context menu items");

            await WebExtensions.ContextMenus.RemoveAll();

            WebExtensions.ContextMenus.Create(new() {
                Id = "launchTab",
                Title = "🌐 Open in tab",
                Contexts = [MenusContextType.Action]
            });

            WebExtensions.ContextMenus.Create(new() {
                Id = "openSidePanel",
                Title = $"📌 Open in side panel",
                Contexts = [MenusContextType.Action]
            });

            logger.LogDebug(nameof(CreateContextMenuItemsAsync) + ": Context menu items created");
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(CreateContextMenuItemsAsync) + ": Error creating context menu items");
        }
    }

    private async Task OpenOrFocusOptionsTabAsync() {
        var optionsUrl = _webExtensionsApi.Runtime.GetURL(Routes.IndexPaths.InTab);

        // Try to reuse existing tab if we have one
        if (_optionsTabId.HasValue) {
            try {
                var existingTab = await WebExtensions.Tabs.Get(_optionsTabId.Value);
                if (existingTab != null) {
                    // Tab exists, focus it
                    await WebExtensions.Tabs.Update(_optionsTabId.Value, new WebExtensions.Net.Tabs.UpdateProperties {
                        Active = true
                    });
                    // Also focus the window containing the tab
                    if (existingTab.WindowId.HasValue) {
                        await WebExtensions.Windows.Update(existingTab.WindowId.Value, new WebExtensions.Net.Windows.UpdateInfo {
                            Focused = true
                        });
                    }
                    return;
                }
            }
            catch {
                // Tab no longer exists, clear the tracked ID
                _optionsTabId = null;
            }
        }

        // Create a new tab and track its ID
        var newTab = await WebExtensions.Tabs.Create(new WebExtensions.Net.Tabs.CreateProperties {
            Url = optionsUrl
        });
        _optionsTabId = newTab.Id;
    }

    //

    // onUpdated fires when the extension is already installed, but updated, which may occur automatically from Chrome and ChromeWebStore, without user intervention
    // This will be triggered by a Chrome Web Store push,
    // or, when sideloading in development, by installing an updated release per the manifest or a Refresh in DevTools.
    private async Task OnInstalledUpdateAsync(string previousVersion) {
        try {
            var currentVersion = WebExtensions.Runtime.GetManifest().GetProperty("version").ToString() ?? DefaultVersion;
            logger.LogInformation(nameof(OnInstalledUpdateAsync) + ": Extension updated from {Previous} to {Current}", previousVersion, currentVersion);

            // Clear BwReadyState first to force fresh initialization after update
            await _storageGateway.RemoveItem<BwReadyState>(StorageArea.Session);

            // TODO P3: EnsureInitializedAsync handles: storage defaults (may need migration), session manager, and BwReadyState

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
            logger.LogError(ex, nameof(OnInstalledUpdateAsync) + ": Error handling update");
        }
    }

    private async Task HandleUnknownMessageActionAsync(string? action) {
        logger.LogWarning(nameof(HandleUnknownMessageActionAsync) + ": Unknown message action: {Action}", action);
        await Task.CompletedTask;
        return;
    }

    private async Task HandleLockAppMessageAsync() {
        try {
            logger.LogInformation(nameof(HandleLockAppMessageAsync) + ": called");
            // The InactivityTimerService handles the actual locking logic
            return;
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleLockAppMessageAsync) + ": Error handling lock app message");
            return;
        }
    }

    private async Task HandleSystemLockDetectedAsync() {
        try {
            logger.LogInformation(nameof(HandleSystemLockDetectedAsync) + ": called");
            logger.LogWarning(nameof(HandleSystemLockDetectedAsync) + ": System lock/suspend/hibernate detected in background worker");

            // Lock the session immediately for security
            await _sessionManager.LockSessionAsync();
            await _storageGateway.SetItem(NextSessionSequence(), StorageArea.Session);
            logger.LogInformation(nameof(HandleSystemLockDetectedAsync) + ": Session locked due to system lock detection");

            // Broadcast lock event to all connected apps via ports
            // Note: Apps will also detect session lock via storage change,
            // but this provides immediate notification
            try {
                await _portService.BroadcastEventToAllAppsAsync(
                    BwAppMessageType.Values.SystemLockDetected,
                    new { reason = "system_lock" });
                logger.LogInformation(nameof(HandleSystemLockDetectedAsync) + ": Broadcasted SystemLockDetected event to all apps via ports");
            }
            catch (Exception ex) {
                logger.LogWarning(ex, nameof(HandleSystemLockDetectedAsync) + ": Could not broadcast SystemLockDetected event (expected if no apps connected)");
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleSystemLockDetectedAsync) + ": Error handling system lock detection");
        }
    }

    private async Task<bool> CheckOriginPermissionAsync(string origin) {
        try {
            logger.LogInformation(nameof(CheckOriginPermissionAsync) + ": Checking permission for origin: {Origin}", origin);

            var anyPermissions = new AnyPermissions {
                Origins = [new MatchPattern(new MatchPatternRestricted(origin))]
            };
            var hasPermission = await WebExtensions.Permissions.Contains(anyPermissions);

            logger.LogInformation(nameof(CheckOriginPermissionAsync) + ": Permission check result for {Origin}: {HasPermission}", origin, hasPermission);
            return hasPermission;
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(CheckOriginPermissionAsync) + ": Error checking origin permission for {Origin}", origin);
            return false;
        }
    }
    private async Task CreateExtensionTabAsync() {
        try {
            var tabUrl = _webExtensionsApi.Runtime.GetURL(Routes.IndexPaths.InTab);
            var cp = new WebExtensions.Net.Tabs.CreateProperties {
                Url = tabUrl
                // TODO P3 use the same tab identifier, so we don't get multiple tabs
            };
            var res = await WebExtensions.Tabs.Create(cp) ?? throw new AggregateException("could not create tab");
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(CreateExtensionTabAsync) + ": Error creating extension tab");
        }
    }

    /// <summary>
    /// Store the pending request for App to retrieve, then uses SidePanel if already open, otherwise action popup for the specified tab.
    /// The App will read the pending request from storage and route to the appropriate page.
    /// </summary>
    /// <param name="pendingRequest">The pending BW→App request to store for App retrieval.</param>

    // TODO P3: See https://chatgpt.com/c/69a724de-ad10-832b-b727-fc4ecf6b5bcf for discussion on refactor to separate the storage of the pending request from the UI logic of choosing SidePanel vs Action popup. This will simplify the logic and make it more reusable.
    private async Task UseSidePanelOrActionPopupAsync(PendingBwAppRequest pendingRequest) {
        try {
            logger.LogInformation(nameof(UseSidePanelOrActionPopupAsync) + ": type={Type}, requestId={RequestId}, tabId={TabId}",
                pendingRequest.Type, pendingRequest.RequestId, pendingRequest.TabId);

            // Store the pending request for App to retrieve
            var addResult = await _pendingBwAppRequestService.AddRequestAsync(pendingRequest);
            if (addResult.IsFailed) {
                logger.LogError(nameof(UseSidePanelOrActionPopupAsync) + ": Failed to store pending request: {Error}",
                    addResult.Errors.Count > 0 ? addResult.Errors[0].Message : "Unknown error");
                return;
            }

            // Notify any already-running App instances to reconnect and check pending work
            logger.LogInformation(nameof(UseSidePanelOrActionPopupAsync) + ": Broadcasting SW_APP_WAKE for requestId={RequestId}", pendingRequest.RequestId);
            try {
                await _jsRuntime.InvokeVoidAsync("chrome.runtime.sendMessage",
                new { t = SendMessageTypes.SwAppWake, requestId = pendingRequest.RequestId });
            }
            catch (Exception ex) {
                // Since it's expected that there may not be any apps connected, can ignore
                logger.LogDebug(ex, nameof(UseSidePanelOrActionPopupAsync) + ": Could not broadcast SW_APP_WAKE event (expected if no apps connected)");
            }

            // Determine if SidePanel is currently open, and if not use Action popup
            var contextFilter = new ContextFilter() { ContextTypes = [ContextType.SIDEPANEL] };
            var contexts = await _webExtensionsApi.Runtime.GetContexts(contextFilter);
            if (!contexts.Any()) {
                logger.LogInformation(nameof(UseSidePanelOrActionPopupAsync) + ": SidePanel context(s) detected, will use SidePanel for request");

                // Note: SetPopup applies globally, not per-tab in Manifest V3
                await WebExtensions.Action.SetPopup(new() {
                    Popup = new(_webExtensionsApi.Runtime.GetURL(Routes.IndexPaths.InPopup))
                });

                // Open popup
                try {
                    WebExtensions.Action.OpenPopup();
                    logger.LogInformation(nameof(UseSidePanelOrActionPopupAsync) + ": succeeded");
                }
                catch (Exception ex) {
                    // Note: openPopup() sometimes throws even when successful
                    logger.LogDebug(ex, nameof(UseSidePanelOrActionPopupAsync) + ": openPopup() exception");
                }

                // Clear the Popup setting so future OpenPopup() invocations will be handled without a tab context
                await WebExtensions.Action.SetPopup(new() {
                    Popup = new WebExtensions.Net.ActionNs.Popup("")
                });
            }
            else {
                logger.LogInformation(nameof(UseSidePanelOrActionPopupAsync) + ": Waiting for SidePanel to detect and handle request");
                // SidePanel, if now or soon opened, will read pending request from storage and navigate accordingly
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(UseSidePanelOrActionPopupAsync) + ": error");
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
                    nameof(ValidateMessageSenderAsync) + ": Message from different extension ID. Expected: {Expected}, Actual: {Actual}",
                    WebExtensions.Runtime.Id,
                    sender?.Id ?? "null"
                );
                return false;
            }

            // 2. Check that sender has a URL
            if (string.IsNullOrEmpty(sender.Url)) {
                logger.LogWarning(nameof(ValidateMessageSenderAsync) + ": Message sender has no URL");
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
                logger.LogWarning(nameof(ValidateMessageSenderAsync) + ": Invalid sender URL: {Url}", sender.Url);
                return false;
            }

            // Extension pages (popup, tab, sidepanel) are always allowed
            var extensionOrigin = $"chrome-extension://{WebExtensions.Runtime.Id}";
            if (senderOrigin.Equals(extensionOrigin, StringComparison.OrdinalIgnoreCase)) {
                // Extension's own pages are trusted
                logger.LogDebug(nameof(ValidateMessageSenderAsync) + ": Message from extension page (trusted)");
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
                    nameof(ValidateMessageSenderAsync) + ": Sender origin not in granted permissions. Origin: {Origin}, Pattern: {Pattern}. " +
                    "This prevents subdomain attacks - only explicitly granted origins can send messages.",
                    senderOrigin,
                    matchPattern
                );
                return false;
            }

            // 4. na Check for documentId (Chrome 106+, recommended but not required)
            // Note: documentId may not be present in all contexts (e.g., messages from service worker)
            // We already validated sender via extension ID, URL, and explicit permissions
            logger.LogDebug(nameof(ValidateMessageSenderAsync) + ": Proceeding with validation. Origin: {Origin}", senderOrigin);

            // 5. Validate payload
            var payloadValid = await ValidatePayloadAsync(messageObj);
            if (!payloadValid) {
                return false;
            }

            // All checks passed
            logger.LogDebug(
                nameof(ValidateMessageSenderAsync) + ": Sender validation passed. Origin: {Origin}",
                senderOrigin
            );
            return true;
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(ValidateMessageSenderAsync) + ": Error validating message sender");
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
            logger.LogWarning(nameof(ValidateMessageSenderAsync) + ": Invalid message payload (null)");
            return Task.FromResult(false);
        }

        // Try to serialize and deserialize to check structure
        try {
            var messageJson = JsonSerializer.Serialize(messageObj);
            var messageDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(messageJson);

            if (messageDict == null) {
                logger.LogWarning(nameof(ValidateMessageSenderAsync) + ": Invalid message payload (not an object)");
                return Task.FromResult(false);
            }

            // Check for type field
            if (!messageDict.TryGetValue("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String) {
                logger.LogWarning(nameof(ValidateMessageSenderAsync) + ": Message payload missing or invalid type field");
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }
        catch (Exception ex) {
            logger.LogWarning(ex, nameof(ValidateMessageSenderAsync) + ": Error validating message payload structure");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Handles RPC requests from ContentScript via port-based messaging.
    /// Routes the request to the appropriate handler based on the RPC method.
    /// </summary>
    private async Task HandleContentScriptRpcAsync(string portId, PortSession? portSession, RpcRequest request) {
        logger.LogInformation(nameof(HandleContentScriptRpcAsync) + ": method={Method}, id={Id}, portId={PortId}",
            request.Method, request.Id, portId);

        await EnsureInitializedAsync();

        if (portSession is null) {
            logger.LogWarning(nameof(HandleContentScriptRpcAsync) + ": No PortSession for portId={PortId}", portId);
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: "No PortSession found for this port");
            return;
        }

        if (!portSession.TabId.HasValue) {
            logger.LogWarning(nameof(HandleContentScriptRpcAsync) + ": No TabId in PortSession for portId={PortId}", portId);
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
                    // Return extension ID and name directly
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                        result: new { extensionId = WebExtensions.Runtime.Id, name = AppConfig.ProductName });
                    return;

                case CsBwMessageTypes.CLEAR_SESSION:
                case CsBwMessageTypes.GET_SESSION_INFO:
                    // Sessions not implemented - respond with specific error
                    logger.LogWarning(nameof(HandleContentScriptRpcAsync) + ": sessions not implemented: {Method}", request.Method);
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                        errorMessage: "Sessions not supported");
                    return;

                case CsBwMessageTypes.GET_CREDENTIAL:
                    // Not implemented yet - respond with specific error
                    logger.LogWarning(nameof(HandleContentScriptRpcAsync) + ": GetCredential not implemented: {Method}", request.Method);
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                        errorMessage: "GetCredential not implemented");
                    return;

                case CsBwMessageTypes.CONFIGURE_VENDOR:
                    // Not implemented - respond with specific error
                    logger.LogWarning(nameof(HandleContentScriptRpcAsync) + ": ConfigureVendor not implemented: {Method}", request.Method);
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                        errorMessage: "ConfigureVendor not supported");
                    return;

                case CsBwMessageTypes.CONNECTION_INVITE:
                    await HandleConnectionInviteRpcAsync(portId, portSession, request, tabId, tabUrl, origin);
                    return;

                case CsBwMessageTypes.CONNECTION_CONFIRM:
                    await HandleConnectionConfirmRpcAsync(portId, portSession, request, tabId, tabUrl);
                    return;

                case CsBwMessageTypes.IPEX_APPLY:
                    await HandleIpexApplyRpcAsync(portId, portSession, request, tabId, tabUrl, origin);
                    return;

                case CsBwMessageTypes.IPEX_AGREE:
                    await HandleIpexAgreeRpcAsync(portId, portSession, request, tabId, tabUrl, origin);
                    return;

                case CsBwMessageTypes.IPEX_ADMIT:
                    await HandleIpexAdmitFromPageRpcAsync(portId, portSession, request, tabId, tabUrl, origin);
                    return;

                case CsBwMessageTypes.INIT:
                    // Legacy method - respond with specific error
                    logger.LogWarning(nameof(HandleContentScriptRpcAsync) + ": Init is legacy/not implemented: {Method}", request.Method);
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                        errorMessage: "Init method is deprecated");
                    return;

                default:
                    logger.LogWarning(nameof(HandleContentScriptRpcAsync) + ": Unknown method: {Method}", request.Method);
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                        errorMessage: $"Unknown method: {request.Method}");
                    return;
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleContentScriptRpcAsync) + ": Error handling ContentScript RPC: method={Method}", request.Method);
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles RPC requests from App (popup/tab/sidepanel) via port-based messaging.
    /// These are typically replies to BW requests or App-initiated actions.
    /// </summary>
    private async Task HandleAppRpcAsync(string portId, PortSession? portSession, RpcRequest request) {
        logger.LogInformation(nameof(HandleAppRpcAsync) + ": method={Method}, id={Id}, portId={PortId}",
            request.Method, request.Id, portId);

        await EnsureInitializedAsync();

        try {
            // Parse the RPC params to extract AppBwMessage fields
            // AppBwPortService sends: { type, requestId, tabId, tabUrl, payload }
            if (request.Params is not JsonElement paramsElement) {
                logger.LogWarning(nameof(HandleAppRpcAsync) + ": Params is not JsonElement");
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: "Invalid params format");
                return;
            }

            // Extract common fields
            var tabId = paramsElement.TryGetProperty("tabId", out var tabIdProp) ? tabIdProp.GetInt32() : 0;
            var tabUrl = paramsElement.TryGetProperty("tabUrl", out var tabUrlProp) ? tabUrlProp.GetString() : null;
            var requestId = paramsElement.TryGetProperty("requestId", out var reqIdProp) ? reqIdProp.GetString() : null;
            var payload = paramsElement.TryGetProperty("payload", out var payloadProp) ? payloadProp : (JsonElement?)null;
            var error = paramsElement.TryGetProperty("error", out var errorProp) ? errorProp.GetString() : null;

            logger.LogDebug(nameof(HandleAppRpcAsync) + ": Parsed params - tabId={TabId}, tabUrl={TabUrl}, requestId={RequestId}",
                tabId, tabUrl, requestId);

            // Extend session on App activity
            if (await _sessionManager.ExtendIfUnlockedAsync()) {
                await _storageGateway.SetItem(NextSessionSequence(), StorageArea.Session);
            }

            // Route based on method (which is the message type)
            switch (request.Method) {
                case AppBwMessageType.Values.ReplyAid:
                case AppBwMessageType.Values.ReplyCredential:
                    await HandleAppReplyAuthorizeRpcAsync(portId, request, tabId, requestId, payload);
                    return;

                case AppBwMessageType.Values.ReplyApprovedSignHeaders:
                    await HandleAppReplySignHeadersRpcAsync(portId, request, tabId, tabUrl, requestId, payload);
                    return;

                case AppBwMessageType.Values.ReplySignData:
                    await HandleAppReplySignDataRpcAsync(portId, request, tabId, requestId, payload);
                    return;

                case AppBwMessageType.Values.ReplyCreateCredential:
                    await HandleAppReplyCreateCredentialRpcAsync(portId, request, tabId, tabUrl, requestId, payload);
                    return;

                case AppBwMessageType.Values.ReplyAidApproval:
                    await HandleAppReplyAidApprovalRpcAsync(portId, request, tabId, requestId, payload);
                    return;

                case AppBwMessageType.Values.ReplySignDataApproval:
                    await HandleAppReplySignDataApprovalRpcAsync(portId, request, tabId, requestId, payload);
                    return;

                case AppBwMessageType.Values.ReplyIpexApplyApproval:
                    await HandleAppReplyIpexApplyApprovalRpcAsync(portId, request, tabId, requestId, payload);
                    return;

                case AppBwMessageType.Values.ReplyIpexAgreeApproval:
                    await HandleAppReplyIpexAgreeApprovalRpcAsync(portId, request, tabId, requestId, payload);
                    return;

                case AppBwMessageType.Values.ReplyIpexAdmitApproval:
                    await HandleAppReplyIpexAdmitApprovalRpcAsync(portId, request, tabId, requestId, payload);
                    return;

                case AppBwMessageType.Values.ReplyCanceled:
                case AppBwMessageType.Values.ReplyError:
                case AppBwMessageType.Values.AppClosed:
                    await HandleAppReplyCanceledRpcAsync(portId, request, tabId, requestId, request.Method, error);
                    return;

                // Note: USER_ACTIVITY is now handled as EVENT (fire-and-forget), not RPC
                // See HandlePortEventAsync for the event handler

                case AppBwMessageType.Values.RequestAddIdentifier:
                    await HandleRequestAddIdentifierRpcAsync(portId, request, payload);
                    return;

                case AppBwMessageType.Values.ResponseToBwRequest:
                    // Legacy BW→App request/response pattern - not used with port-based messaging
                    logger.LogInformation(nameof(HandleAppRpcAsync) + ": ResponseToBwRequest received for requestId={RequestId} (legacy path)", requestId);
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id, result: new { success = true });
                    return;

                case AppBwMessageType.Values.RequestHealthCheck:
                    await HandleAppRequestHealthCheckRpcAsync(portId, request, payload);
                    return;

                case AppBwMessageType.Values.RequestConnect:
                    await HandleAppRequestConnectRpcAsync(portId, request, payload);
                    return;

                case AppBwMessageType.Values.RequestGetCredentials:
                    await HandleAppRequestGetCredentialsRpcAsync(portId, request);
                    return;

                case AppBwMessageType.Values.RequestGetKeyState:
                    await HandleAppRequestGetKeyStateRpcAsync(portId, request, payload);
                    return;

                case AppBwMessageType.Values.RequestGetKeyEvents:
                    await HandleAppRequestGetKeyEventsRpcAsync(portId, request, payload);
                    return;

                case AppBwMessageType.Values.RequestRenameAid:
                    await HandleAppRequestRenameAidRpcAsync(portId, request, payload);
                    return;

                case AppBwMessageType.Values.RequestConfigure:
                    await HandleAppRequestConfigureRpcAsync(portId, request, payload);
                    return;

                case AppBwMessageType.Values.RequestResetConfigure:
                    await HandleAppRequestResetConfigureRpcAsync(portId, request);
                    return;

                case AppBwMessageType.Values.RequestPrimeDataGo:
                    await HandleAppRequestPrimeDataGoRpcAsync(portId, request, payload);
                    return;

                case AppBwMessageType.Values.RequestPrimeDataIpex:
                    await HandleAppRequestPrimeDataIpexRpcAsync(portId, request, payload);
                    return;

                case AppBwMessageType.Values.RequestIpexEligibleDisclosers:
                    await HandleAppRequestIpexEligibleDisclosersRpcAsync(portId, request, payload);
                    return;

                case AppBwMessageType.Values.RequestGetOobi:
                    await HandleAppRequestGetOobiRpcAsync(portId, request, payload);
                    return;

                case AppBwMessageType.Values.RequestResolveOobi:
                    await HandleAppRequestResolveOobiRpcAsync(portId, request, payload);
                    return;

                case AppBwMessageType.Values.ReplyConnectionInvite:
                    await HandleAppReplyConnectionInviteRpcAsync(portId, request, tabId, requestId, payload);
                    return;

                case AppBwMessageType.Values.RequestGetExchange:
                    await HandleAppRequestGetExchangeRpcAsync(portId, request, payload);
                    return;

                case AppBwMessageType.Values.RequestIpexAdmit:
                    await HandleAppRequestIpexAdmitRpcAsync(portId, request, payload);
                    return;

                case AppBwMessageType.Values.RequestIpexAgree:
                    await HandleAppRequestIpexAgreeRpcAsync(portId, request, payload);
                    return;

                case AppBwMessageType.Values.RequestIpexOffer:
                    await HandleAppRequestIpexOfferRpcAsync(portId, request, payload);
                    return;

                case AppBwMessageType.Values.RequestIpexGrant:
                    await HandleAppRequestIpexGrantRpcAsync(portId, request, payload);
                    return;

                case AppBwMessageType.Values.RequestIpexGrantPresentation:
                    await HandleAppRequestIpexOfferOrGrantPresentationRpcAsync(portId, request, payload);
                    return;

                case AppBwMessageType.Values.RequestUnlockSession:
                    await HandleAppRequestUnlockSessionRpcAsync(portId, request, payload);
                    return;

                case AppBwMessageType.Values.RequestGetSessionPasscode:
                    await HandleAppRequestGetSessionPasscodeRpcAsync(portId, request);
                    return;

                default:
                    logger.LogWarning(nameof(HandleAppRpcAsync) + ": Unknown method: {Method}", request.Method);
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                        errorMessage: $"Unknown method: {request.Method}");
                    return;
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppRpcAsync) + ": Error handling App RPC: method={Method}", request.Method);
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles ReplyAid and ReplyCredential RPC from App.
    /// Transforms to polaris-web format and forwards to ContentScript.
    /// </summary>
    private async Task HandleAppReplyAuthorizeRpcAsync(string portId, RpcRequest request, int tabId, string? requestId, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppReplyAuthorizeRpcAsync) + ": tabId={TabId}, requestId={RequestId}", tabId, requestId);

        if (requestId is null) {
            logger.LogWarning(nameof(HandleAppReplyAuthorizeRpcAsync) + ": Missing requestId");
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
            logger.LogInformation(nameof(HandleAppReplyAuthorizeRpcAsync) + ": Sending RPC response for authorize, requestId={RequestId}", requestId);
            await _portService.SendRpcResponseAsync(
                pendingRequest.PortId,
                pendingRequest.PortSessionId,
                pendingRequest.RpcRequestId ?? requestId,
                result: transformedPayload);
        }
        else {
            logger.LogWarning(nameof(HandleAppReplyAuthorizeRpcAsync) + ": No port info for authorize response, requestId={RequestId}. Response not sent.", requestId);
        }

        // Acknowledge the App RPC
        await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id, result: new { success = true });
    }

    /// <summary>
    /// Handles ReplyAidApproval RPC from App.
    /// App sends just the identifier prefix and optionally a credential SAID.
    /// BackgroundWorker fetches the credential and CESR representation, then forwards to ContentScript.
    /// </summary>
    private async Task HandleAppReplyAidApprovalRpcAsync(string portId, RpcRequest request, int tabId, string? requestId, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppReplyAidApprovalRpcAsync) + ": tabId={TabId}, requestId={RequestId}", tabId, requestId);

        if (requestId is null) {
            logger.LogWarning(nameof(HandleAppReplyAidApprovalRpcAsync) + ": Missing requestId");
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

        try {
            // Deserialize payload to AidApprovalPayload
            if (payload is null || !payload.HasValue) {
                logger.LogWarning(nameof(HandleAppReplyAidApprovalRpcAsync) + ": Missing payload");
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: "Missing payload");
                return;
            }

            var approvalPayload = JsonSerializer.Deserialize<AidApprovalPayload>(payload.Value.GetRawText(), JsonOptions.CamelCase);
            if (approvalPayload is null) {
                logger.LogWarning(nameof(HandleAppReplyAidApprovalRpcAsync) + ": Could not deserialize AidApprovalPayload");
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: "Invalid approval payload");
                return;
            }

            logger.LogInformation(nameof(HandleAppReplyAidApprovalRpcAsync) + ": prefix={Prefix}, alias={Alias}, credentialSaid={Said}",
                approvalPayload.Prefix, approvalPayload.Alias, approvalPayload.CredentialSaid ?? "null");

            // Build the identifier part
            var identifier = new BwCsAuthorizeResultIdentifier(
                Prefix: approvalPayload.Prefix,
                Name: approvalPayload.Alias
            );

            // If credential SAID is provided, fetch the credential and its CESR representation
            BwCsAuthorizeResultCredential? credential = null;
            if (!string.IsNullOrEmpty(approvalPayload.CredentialSaid)) {
                try {
                    // Fetch credentials from signify-ts
                    var credentialsResult = await _broker.EnqueueReadAsync(SignifyOperation.GetCredentials,
                        svc => svc.GetCredentials());
                    if (credentialsResult.IsFailed) {
                        logger.LogWarning(nameof(HandleAppReplyAidApprovalRpcAsync) + ": Failed to fetch credentials: {Error}",
                            credentialsResult.Errors.Count > 0 ? credentialsResult.Errors[0].Message : "Unknown error");
                    }
                    else if (credentialsResult.Value is not null) {
                        // Find the credential by SAID
                        RecursiveDictionary? foundCredential = null;
                        foreach (var cred in credentialsResult.Value) {
                            var said = cred.GetByPath("sad.d")?.StringValue;
                            if (said == approvalPayload.CredentialSaid) {
                                foundCredential = cred;
                                break;
                            }
                        }

                        if (foundCredential is not null) {
                            // Get CESR representation — unwrap {ok, value} envelope from signifyClient.ts
                            var cesrResultJson = await _signifyClientBinding.GetCredentialAsync(approvalPayload.CredentialSaid, true);
                            using var cesrDoc = JsonDocument.Parse(cesrResultJson);
                            var cesrRoot = cesrDoc.RootElement;
                            string? cesr = null;
                            if (cesrRoot.TryGetProperty("ok", out var cesrOk) && cesrOk.GetBoolean() && cesrRoot.TryGetProperty("value", out var cesrValue)) {
                                cesr = cesrValue.ValueKind == JsonValueKind.String ? cesrValue.GetString() : cesrValue.GetRawText();
                            }
                            else {
                                logger.LogWarning(nameof(HandleAppReplyAidApprovalRpcAsync) + ": Failed to get CESR: {Json}", cesrResultJson);
                            }
                            credential = cesr is not null
                                ? new BwCsAuthorizeResultCredential(Raw: foundCredential, Cesr: cesr)
                                : null;
                            logger.LogInformation(nameof(HandleAppReplyAidApprovalRpcAsync) + ": Found credential and fetched CESR");
                        }
                        else {
                            logger.LogWarning(nameof(HandleAppReplyAidApprovalRpcAsync) + ": Credential not found for SAID={Said}", approvalPayload.CredentialSaid);
                        }
                    }
                }
                catch (Exception ex) {
                    logger.LogError(ex, nameof(HandleAppReplyAidApprovalRpcAsync) + ": Error fetching credential/CESR");
                    // Continue without credential - still send identifier
                }
            }

            // Build polaris-web response
            var result = new BwCsAuthorizeResultPayload(
                Identifier: identifier,
                Credential: credential
            );

            // Route response to ContentScript via port
            if (pendingRequest?.PortId is not null && pendingRequest.PortSessionId is not null) {
                logger.LogInformation(nameof(HandleAppReplyAidApprovalRpcAsync) + ": Sending RPC response for AID approval, requestId={RequestId}", requestId);
                await _portService.SendRpcResponseAsync(
                    pendingRequest.PortId,
                    pendingRequest.PortSessionId,
                    pendingRequest.RpcRequestId ?? requestId,
                    result: result);
            }
            else {
                logger.LogWarning(nameof(HandleAppReplyAidApprovalRpcAsync) + ": No port info for AID approval response, requestId={RequestId}. Response not sent.", requestId);
            }

            // Acknowledge the App RPC
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id, result: new { success = true });
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppReplyAidApprovalRpcAsync) + ": Error processing approval");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles ReplySignDataApproval RPC from App.
    /// App sends the identifier prefix and data items to sign.
    /// BackgroundWorker performs the actual signing via signify-ts and forwards result to ContentScript.
    /// </summary>
    private async Task HandleAppReplySignDataApprovalRpcAsync(string portId, RpcRequest request, int tabId, string? requestId, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppReplySignDataApprovalRpcAsync) + ": tabId={TabId}, requestId={RequestId}", tabId, requestId);

        if (requestId is null) {
            logger.LogWarning(nameof(HandleAppReplySignDataApprovalRpcAsync) + ": Missing requestId");
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

        try {
            // Deserialize payload to SignDataApprovalPayload
            if (payload is null || !payload.HasValue) {
                logger.LogWarning(nameof(HandleAppReplySignDataApprovalRpcAsync) + ": Missing payload");
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: "Missing payload");
                return;
            }

            var approvalPayload = JsonSerializer.Deserialize<SignDataApprovalPayload>(payload.Value.GetRawText(), JsonOptions.CamelCase);
            if (approvalPayload is null) {
                logger.LogWarning(nameof(HandleAppReplySignDataApprovalRpcAsync) + ": Could not deserialize SignDataApprovalPayload");
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: "Invalid approval payload");
                return;
            }

            logger.LogInformation(nameof(HandleAppReplySignDataApprovalRpcAsync) + ": prefix={Prefix}, itemCount={Count}",
                approvalPayload.Prefix, approvalPayload.DataItems?.Length ?? 0);

            // Perform the signing via signify-ts
            var dataItemsJson = JsonSerializer.Serialize(approvalPayload.DataItems);
            var signResultJson = await _signifyClientBinding.SignDataAsync(approvalPayload.Prefix, dataItemsJson);

            logger.LogDebug(nameof(HandleAppReplySignDataApprovalRpcAsync) + ": signResultJson={Result}", signResultJson);

            // Parse the result — signifyClient.ts wraps results in {ok, value} envelope
            using var jsResultDoc = JsonDocument.Parse(signResultJson);
            var jsRoot = jsResultDoc.RootElement;
            if (!jsRoot.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean()) {
                var errMsg = jsRoot.TryGetProperty("message", out var msgProp) ? msgProp.GetString() : "Sign data failed";
                logger.LogWarning(nameof(HandleAppReplySignDataApprovalRpcAsync) + ": JS error: {Error}", errMsg);
                if (pendingRequest?.PortId is not null && pendingRequest.PortSessionId is not null) {
                    await _portService.SendRpcResponseAsync(
                        pendingRequest.PortId,
                        pendingRequest.PortSessionId,
                        pendingRequest.RpcRequestId ?? requestId,
                        errorMessage: errMsg);
                }
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: errMsg);
                return;
            }
            var valueJson = jsRoot.GetProperty("value").GetRawText();
            var signResult = JsonSerializer.Deserialize<SignDataResult>(valueJson, JsonOptions.Default);

            if (signResult is null) {
                logger.LogWarning(nameof(HandleAppReplySignDataApprovalRpcAsync) + ": Failed to parse sign data result");
                // Send error to ContentScript
                if (pendingRequest?.PortId is not null && pendingRequest.PortSessionId is not null) {
                    await _portService.SendRpcResponseAsync(
                        pendingRequest.PortId,
                        pendingRequest.PortSessionId,
                        pendingRequest.RpcRequestId ?? requestId,
                        errorMessage: "Failed to sign data");
                }
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: "Failed to sign data");
                return;
            }

            logger.LogInformation(nameof(HandleAppReplySignDataApprovalRpcAsync) + ": Signed {Count} items with AID={Aid}",
                signResult.Items?.Length ?? 0, signResult.Aid);

            // Route response to ContentScript via port
            if (pendingRequest?.PortId is not null && pendingRequest.PortSessionId is not null) {
                logger.LogInformation(nameof(HandleAppReplySignDataApprovalRpcAsync) + ": Sending RPC response for sign-data approval, requestId={RequestId}", requestId);
                await _portService.SendRpcResponseAsync(
                    pendingRequest.PortId,
                    pendingRequest.PortSessionId,
                    pendingRequest.RpcRequestId ?? requestId,
                    result: signResult);
            }
            else {
                logger.LogWarning(nameof(HandleAppReplySignDataApprovalRpcAsync) + ": No port info for sign-data approval response, requestId={RequestId}. Response not sent.", requestId);
            }

            // Acknowledge the App RPC
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id, result: new { success = true });
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppReplySignDataApprovalRpcAsync) + ": Error signing data");
            // Try to notify ContentScript of the error
            if (pendingRequest?.PortId is not null && pendingRequest.PortSessionId is not null) {
                await _portService.SendRpcResponseAsync(
                    pendingRequest.PortId,
                    pendingRequest.PortSessionId,
                    pendingRequest.RpcRequestId ?? requestId,
                    errorMessage: $"Error signing data: {ex.Message}");
            }
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles ReplyIpexApplyApproval RPC from App.
    /// User approved the IPEX apply request. Sends the apply message via signify-ts
    /// and routes the result back to ContentScript.
    /// </summary>
    private async Task HandleAppReplyIpexApplyApprovalRpcAsync(string portId, RpcRequest request, int tabId, string? requestId, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppReplyIpexApplyApprovalRpcAsync) + ": tabId={TabId}, requestId={RequestId}", tabId, requestId);

        if (requestId is null) {
            logger.LogWarning(nameof(HandleAppReplyIpexApplyApprovalRpcAsync) + ": Missing requestId");
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

        try {
            if (payload is null || !payload.HasValue) {
                logger.LogWarning(nameof(HandleAppReplyIpexApplyApprovalRpcAsync) + ": Missing payload");
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: "Missing payload");
                return;
            }

            var approvalPayload = JsonSerializer.Deserialize<IpexApplyApprovalPayload>(
                payload.Value.GetRawText(), JsonOptions.CamelCase);

            if (approvalPayload is null || string.IsNullOrEmpty(approvalPayload.SenderPrefix)
                || string.IsNullOrEmpty(approvalPayload.RecipientPrefix) || string.IsNullOrEmpty(approvalPayload.SchemaSaid)) {
                logger.LogWarning(nameof(HandleAppReplyIpexApplyApprovalRpcAsync) + ": Invalid approval payload");
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: "Invalid or missing IPEX apply approval parameters");
                return;
            }

            // Acknowledge the App RPC immediately (before long operation)
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id, result: new { success = true });

            // Route IPEX response to ContentScript via stored port info
            if (pendingRequest?.PortId is not null && pendingRequest.PortSessionId is not null) {
                await ExecuteIpexAndRespondAsync(
                    pendingRequest.PortId, pendingRequest.PortSessionId,
                    pendingRequest.RpcRequestId ?? requestId,
                    operationName: nameof(HandleAppReplyIpexApplyApprovalRpcAsync),
                    brokerOp: SignifyOperation.IpexApplyAndSubmit,
                    operation: () => _signifyClientService.IpexApplyAndSubmit(new IpexApplySubmitArgs(
                        SenderNameOrPrefix: approvalPayload.SenderPrefix,
                        RecipientPrefix: approvalPayload.RecipientPrefix,
                        SchemaSaid: approvalPayload.SchemaSaid,
                        Attributes: approvalPayload.Attributes
                    )),
                    saidKey: "applySaid",
                    senderPrefix: approvalPayload.SenderPrefix
                );
            }
            else {
                logger.LogWarning(nameof(HandleAppReplyIpexApplyApprovalRpcAsync) + ": No port info for response routing, requestId={RequestId}", requestId);
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppReplyIpexApplyApprovalRpcAsync) + ": Error");
            if (pendingRequest?.PortId is not null && pendingRequest.PortSessionId is not null) {
                await _portService.SendRpcResponseAsync(
                    pendingRequest.PortId, pendingRequest.PortSessionId,
                    pendingRequest.RpcRequestId ?? requestId,
                    errorMessage: $"Error sending IPEX apply: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Handles ReplyIpexAgreeApproval RPC from App.
    /// User approved the IPEX agree request. Sends the agree message via signify-ts
    /// and routes the result back to ContentScript.
    /// </summary>
    private async Task HandleAppReplyIpexAgreeApprovalRpcAsync(string portId, RpcRequest request, int tabId, string? requestId, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppReplyIpexAgreeApprovalRpcAsync) + ": tabId={TabId}, requestId={RequestId}", tabId, requestId);

        if (requestId is null) {
            logger.LogWarning(nameof(HandleAppReplyIpexAgreeApprovalRpcAsync) + ": Missing requestId");
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

        try {
            if (payload is null || !payload.HasValue) {
                logger.LogWarning(nameof(HandleAppReplyIpexAgreeApprovalRpcAsync) + ": Missing payload");
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: "Missing payload");
                return;
            }

            var approvalPayload = JsonSerializer.Deserialize<IpexAgreeApprovalPayload>(
                payload.Value.GetRawText(), JsonOptions.CamelCase);

            if (approvalPayload is null || string.IsNullOrEmpty(approvalPayload.SenderPrefix)
                || string.IsNullOrEmpty(approvalPayload.RecipientPrefix) || string.IsNullOrEmpty(approvalPayload.OfferSaid)) {
                logger.LogWarning(nameof(HandleAppReplyIpexAgreeApprovalRpcAsync) + ": Invalid approval payload");
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: "Invalid or missing IPEX agree approval parameters");
                return;
            }

            // Acknowledge the App RPC immediately (before long operation)
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id, result: new { success = true });

            // Route IPEX response to ContentScript via stored port info
            // Note: schema OOBIs should already be resolved proactively by notification polling (OnSchemasNeeded callback)
            if (pendingRequest?.PortId is not null && pendingRequest.PortSessionId is not null) {
                await ExecuteIpexAndRespondAsync(
                    pendingRequest.PortId, pendingRequest.PortSessionId,
                    pendingRequest.RpcRequestId ?? requestId,
                    operationName: nameof(HandleAppReplyIpexAgreeApprovalRpcAsync),
                    brokerOp: SignifyOperation.IpexAgreeAndSubmit,
                    operation: () => _signifyClientService.IpexAgreeAndSubmit(new IpexAgreeSubmitArgs(
                        SenderNameOrPrefix: approvalPayload.SenderPrefix,
                        RecipientPrefix: approvalPayload.RecipientPrefix,
                        OfferSaid: approvalPayload.OfferSaid
                    )),
                    saidKey: "agreeSaid",
                    senderPrefix: approvalPayload.SenderPrefix
                );
            }
            else {
                logger.LogWarning(nameof(HandleAppReplyIpexAgreeApprovalRpcAsync) + ": No port info for response routing, requestId={RequestId}", requestId);
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppReplyIpexAgreeApprovalRpcAsync) + ": Error");
            if (pendingRequest?.PortId is not null && pendingRequest.PortSessionId is not null) {
                await _portService.SendRpcResponseAsync(
                    pendingRequest.PortId, pendingRequest.PortSessionId,
                    pendingRequest.RpcRequestId ?? requestId,
                    errorMessage: $"Error sending IPEX agree: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Handles ReplyIpexAdmitApproval RPC from App (webpage-initiated).
    /// User approved the IPEX admit request. Sends the admit message via signify-ts
    /// and routes the result back to ContentScript.
    /// </summary>
    private async Task HandleAppReplyIpexAdmitApprovalRpcAsync(string portId, RpcRequest request, int tabId, string? requestId, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppReplyIpexAdmitApprovalRpcAsync) + ": tabId={TabId}, requestId={RequestId}", tabId, requestId);

        if (requestId is null) {
            logger.LogWarning(nameof(HandleAppReplyIpexAdmitApprovalRpcAsync) + ": Missing requestId");
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

        try {
            if (payload is null || !payload.HasValue) {
                logger.LogWarning(nameof(HandleAppReplyIpexAdmitApprovalRpcAsync) + ": Missing payload");
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: "Missing payload");
                return;
            }

            var approvalPayload = JsonSerializer.Deserialize<IpexAdmitApprovalPayload>(
                payload.Value.GetRawText(), JsonOptions.CamelCase);

            if (approvalPayload is null || string.IsNullOrEmpty(approvalPayload.SenderPrefix)
                || string.IsNullOrEmpty(approvalPayload.RecipientPrefix) || string.IsNullOrEmpty(approvalPayload.GrantSaid)) {
                logger.LogWarning(nameof(HandleAppReplyIpexAdmitApprovalRpcAsync) + ": Invalid approval payload");
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: "Invalid or missing IPEX admit approval parameters");
                return;
            }

            // Acknowledge the App RPC immediately (before long operation)
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id, result: new { success = true });

            // Route IPEX response to ContentScript via stored port info (may take 60+ seconds for admit)
            // Note: schema OOBIs should already be resolved proactively by notification polling (OnSchemasNeeded callback)
            logger.LogInformation(nameof(HandleAppReplyIpexAdmitApprovalRpcAsync) +
                ": Starting IpexAdmitAndSubmit, sender={Sender}, recipient={Recipient}, grantSaid={GrantSaid}",
                approvalPayload.SenderPrefix, approvalPayload.RecipientPrefix, approvalPayload.GrantSaid);

            if (pendingRequest?.PortId is not null && pendingRequest.PortSessionId is not null) {
                await ExecuteIpexAndRespondAsync(
                    pendingRequest.PortId, pendingRequest.PortSessionId,
                    pendingRequest.RpcRequestId ?? requestId,
                    operationName: nameof(HandleAppReplyIpexAdmitApprovalRpcAsync),
                    brokerOp: SignifyOperation.IpexAdmitAndSubmit,
                    operation: () => _signifyClientService.IpexAdmitAndSubmit(new IpexAdmitSubmitArgs(
                        SenderNameOrPrefix: approvalPayload.SenderPrefix,
                        RecipientPrefix: approvalPayload.RecipientPrefix,
                        GrantSaid: approvalPayload.GrantSaid
                    )),
                    saidKey: "admitSaid",
                    senderPrefix: approvalPayload.SenderPrefix,
                    onSuccess: async () => {
                        await PollNotificationsThrottledAsync();
                        await RefreshCachedCredentialsAsync();
                    }
                );
            }
            else {
                logger.LogWarning(nameof(HandleAppReplyIpexAdmitApprovalRpcAsync) + ": No port info for response routing, requestId={RequestId}", requestId);
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppReplyIpexAdmitApprovalRpcAsync) + ": Error");
            if (pendingRequest?.PortId is not null && pendingRequest.PortSessionId is not null) {
                await _portService.SendRpcResponseAsync(
                    pendingRequest.PortId, pendingRequest.PortSessionId,
                    pendingRequest.RpcRequestId ?? requestId,
                    errorMessage: $"Error sending IPEX admit: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Handles ReplyApprovedSignHeaders RPC from App.
    /// Signs the headers and forwards the result to ContentScript.
    /// </summary>
    private async Task HandleAppReplySignHeadersRpcAsync(string portId, RpcRequest request, int tabId, string? tabUrl, string? requestId, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppReplySignHeadersRpcAsync) + ": tabId={TabId}, requestId={RequestId}", tabId, requestId);

        if (payload is null || requestId is null || tabUrl is null) {
            logger.LogWarning(nameof(HandleAppReplySignHeadersRpcAsync) + ": Missing required fields");
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
                logger.LogWarning(nameof(HandleAppReplySignHeadersRpcAsync) + ": Could not deserialize payload to AppBwReplySignPayload2");
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: "Invalid sign payload");
                return;
            }

            logger.LogInformation(nameof(HandleAppReplySignHeadersRpcAsync) + ": origin={Origin}, url={Url}, method={Method}",
                signPayload.Origin, signPayload.Url, signPayload.Method);

            // Sign and send - this will forward the result to CS
            await SignAndSendRequestHeaders(
                new AppBwReplySignMessage(tabId, tabUrl, requestId, signPayload.Origin, signPayload.Url, signPayload.Method, signPayload.Headers, signPayload.Prefix),
                pendingRequest?.PortId,
                pendingRequest?.PortSessionId,
                pendingRequest?.RpcRequestId);

            // Acknowledge the App RPC
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id, result: new { success = true });
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppReplySignHeadersRpcAsync) + ": Error handling sign headers");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles ReplySignData RPC from App.
    /// Forwards the signed data result to ContentScript.
    /// </summary>
    private async Task HandleAppReplySignDataRpcAsync(string portId, RpcRequest request, int tabId, string? requestId, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppReplySignDataRpcAsync) + ": tabId={TabId}, requestId={RequestId}", tabId, requestId);

        if (requestId is null) {
            logger.LogWarning(nameof(HandleAppReplySignDataRpcAsync) + ": Missing requestId");
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
                    logger.LogInformation(nameof(HandleAppReplySignDataRpcAsync) + ": aid={Aid}, itemCount={Count}",
                        signDataResult.Aid, signDataResult.Items?.Length ?? 0);
                }
                else {
                    errorStr = "Failed to process sign-data result";
                }
            }
            catch (Exception ex) {
                logger.LogError(ex, nameof(HandleAppReplySignDataRpcAsync) + ": Error deserializing SignDataResult");
                errorStr = "Error processing sign-data result";
            }
        }

        // Route response to ContentScript
        if (pendingRequest?.PortId is not null && pendingRequest.PortSessionId is not null) {
            logger.LogInformation(nameof(HandleAppReplySignDataRpcAsync) + ": Sending RPC response for sign-data, requestId={RequestId}", requestId);
            await _portService.SendRpcResponseAsync(
                pendingRequest.PortId,
                pendingRequest.PortSessionId,
                pendingRequest.RpcRequestId ?? requestId,
                result: errorStr is null ? transformedPayload : null,
                errorMessage: errorStr);
        }
        else {
            logger.LogWarning(nameof(HandleAppReplySignDataRpcAsync) + ": No port info for sign-data response, requestId={RequestId}. Response not sent.", requestId);
        }

        // Acknowledge the App RPC
        await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id, result: new { success = true });
    }

    /// <summary>
    /// Handles ReplyCreateCredential RPC from App.
    /// Issues the credential via signify-ts and forwards the result to ContentScript.
    /// </summary>
    private async Task HandleAppReplyCreateCredentialRpcAsync(string portId, RpcRequest request, int tabId, string? tabUrl, string? requestId, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppReplyCreateCredentialRpcAsync) + ": tabId={TabId}, requestId={RequestId}", tabId, requestId);

        if (requestId is null || payload is null) {
            logger.LogWarning(nameof(HandleAppReplyCreateCredentialRpcAsync) + ": Missing required fields");
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
    private async Task HandleAppReplyCanceledRpcAsync(string portId, RpcRequest request, int tabId, string? requestId, string messageType, string? errorFromApp) {
        logger.LogInformation(nameof(HandleAppReplyCanceledRpcAsync) + ": type={Type}, tabId={TabId}, requestId={RequestId}, error={Error}",
            messageType, tabId, requestId, errorFromApp);

        // Use the error message from App if provided, otherwise fall back to defaults based on message type
        string errorStr = !string.IsNullOrEmpty(errorFromApp)
            ? errorFromApp
            : messageType switch {
                AppBwMessageType.Values.ReplyError => $"An error occurred in the {AppConfig.ProductName} app",
                AppBwMessageType.Values.AppClosed => $"The {AppConfig.ProductName} app was closed",
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
            logger.LogInformation(nameof(HandleAppReplyCanceledRpcAsync) + ": Sending cancel RPC response, requestId={RequestId}", requestId);
            await _portService.SendRpcResponseAsync(
                pendingRequest.PortId,
                pendingRequest.PortSessionId,
                pendingRequest.RpcRequestId ?? requestId ?? "",
                errorMessage: errorStr);
        }
        else {
            logger.LogWarning(nameof(HandleAppReplyCanceledRpcAsync) + ": No port info for cancel response, requestId={RequestId}. Response not sent.", requestId);
        }

        // Acknowledge the App RPC
        await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id, result: new { success = true });
    }

    /// <summary>
    /// Handles UnlockSession RPC from App.
    /// Validates passcode, stores in SessionManager memory, writes SessionStateModel, and connects signify-ts.
    /// </summary>
    private async Task HandleAppRequestUnlockSessionRpcAsync(string portId, RpcRequest request, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppRequestUnlockSessionRpcAsync) + ": called");

        try {
            // Extract passcode from payload
            if (payload is null) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: "Missing payload");
                return;
            }

            if (!payload.Value.TryGetProperty("passcode", out var passcodeProp)) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: "Missing passcode in payload");
                return;
            }

            var passcode = passcodeProp.GetString();
            if (string.IsNullOrEmpty(passcode)) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: "Passcode is empty");
                return;
            }

            // Unlock session (validates hash, stores in memory, writes SessionStateModel)
            var unlockResult = await _sessionManager.UnlockSessionAsync(passcode);
            if (unlockResult.IsSuccess) {
                await _storageGateway.SetItem(NextSessionSequence(), StorageArea.Session);
            }
            if (unlockResult.IsFailed) {
                var errorMsg = unlockResult.Errors.Count > 0 ? unlockResult.Errors[0].Message : "Unlock failed";
                logger.LogWarning(nameof(HandleAppRequestUnlockSessionRpcAsync) + ": Unlock failed: {Error}", errorMsg);
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: errorMsg);
                return;
            }

            // Fire off KERIA connect in background — don't block the unlock response.
            // App will navigate to ConnectingPage which sends RequestConnect;
            // that handler awaits _pendingConnectTask if still in progress.
            _pendingConnectTask = TryConnectSignifyClientAsync();
            _ = _pendingConnectTask.ContinueWith(t => {
                if (t.Result.IsFailed) {
                    var errorMsg = t.Result.Errors.Count > 0 ? t.Result.Errors[0].Message : "KERIA connect failed";
                    logger.LogWarning(nameof(HandleAppRequestUnlockSessionRpcAsync) + ": Background KERIA connect failed: {Error}", errorMsg);
                }
                _pendingConnectTask = null;
            }, TaskScheduler.Current);

            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new { success = true });
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppRequestUnlockSessionRpcAsync) + ": Error during unlock");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles GetSessionPasscode RPC from App.
    /// Returns the in-memory passcode if the session is unlocked, or an error if locked.
    /// Used by App pages that need the passcode (e.g., MnemonicPage, WebauthnService).
    /// </summary>
    private async Task HandleAppRequestGetSessionPasscodeRpcAsync(string portId, RpcRequest request) {
        var passcode = _sessionManager.GetPasscode();
        if (string.IsNullOrEmpty(passcode)) {
            logger.LogWarning(nameof(HandleAppRequestGetSessionPasscodeRpcAsync) + ": Session is locked — passcode not available");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new GetSessionPasscodeResponsePayload(false, Error: "Session is locked"));
            return;
        }
        await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
            result: new GetSessionPasscodeResponsePayload(true, Passcode: passcode));
    }

    /// <summary>
    /// Handles health check request from App.
    /// Calls KERIA health endpoint and returns the result.
    /// </summary>
    private async Task HandleAppRequestHealthCheckRpcAsync(string portId, RpcRequest request, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppRequestHealthCheckRpcAsync) + ": called");

        try {
            if (!payload.HasValue) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new HealthCheckResponsePayload(false, "Missing payload"));
                return;
            }

            var healthCheckRequest = JsonSerializer.Deserialize<HealthCheckRequestPayload>(
                payload.Value.GetRawText(), JsonOptions.CamelCase);

            if (healthCheckRequest is null || string.IsNullOrEmpty(healthCheckRequest.HealthUrl)) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new HealthCheckResponsePayload(false, "Invalid health check URL"));
                return;
            }

            var healthUri = new Uri(healthCheckRequest.HealthUrl);
            var healthCheckResult = await _broker.EnqueueReadAsync(SignifyOperation.HealthCheck,
                svc => svc.HealthCheck(healthUri));

            if (healthCheckResult.IsSuccess) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new HealthCheckResponsePayload(true));
            }
            else {
                var errorMsg = healthCheckResult.Errors.Count > 0 ? healthCheckResult.Errors[0].Message : "Health check failed";
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new HealthCheckResponsePayload(false, errorMsg));
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppRequestHealthCheckRpcAsync) + ": Error during health check");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new HealthCheckResponsePayload(false, ex.Message));
        }
    }

    /// <summary>
    /// Handles connect request from App.
    /// Connects to KERIA and returns the controller/agent prefixes.
    /// </summary>
    private async Task HandleAppRequestConnectRpcAsync(string portId, RpcRequest request, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppRequestConnectRpcAsync) + ": called");

        try {
            if (!payload.HasValue) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new ConnectResponsePayload(false, Error: "Missing payload"));
                return;
            }

            var connectRequest = JsonSerializer.Deserialize<ConnectRequestPayload>(
                payload.Value.GetRawText(), JsonOptions.CamelCase);

            if (connectRequest is null || string.IsNullOrEmpty(connectRequest.AdminUrl)) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new ConnectResponsePayload(false, Error: "Invalid connect parameters"));
                return;
            }

            // Use the provided passcode, or fall back to the in-memory passcode (reconnect path)
            var passcode = string.IsNullOrEmpty(connectRequest.Passcode)
                ? _sessionManager.GetPasscode()
                : connectRequest.Passcode;

            if (string.IsNullOrEmpty(passcode)) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new ConnectResponsePayload(false, Error: "No passcode available — session may be locked"));
                return;
            }

            // If already connected (e.g., unlock path already called TryConnectSignifyClientAsync),
            // skip the redundant Connect which would destructively reset the JS _client to null
            // and race with in-flight notification polling calls.
            Result<State> connectResult;
            var skippedConnect = false;
            if (_signifyClientService.IsConnected && !connectRequest.IsNewAgent) {
                logger.LogInformation(nameof(HandleAppRequestConnectRpcAsync) + ": Already connected, skipping redundant Connect");
                skippedConnect = true;
                var stateResult = await _broker.EnqueueReadAsync(SignifyOperation.GetState, svc => svc.GetState());
                connectResult = stateResult.IsSuccess
                    ? Result.Ok(stateResult.Value)
                    : Result.Fail<State>(stateResult.Errors);
            }
            else if (_pendingConnectTask is not null && !connectRequest.IsNewAgent) {
                // Unlock handler already started a background connect — await it
                // instead of starting a destructive second connect.
                logger.LogInformation(nameof(HandleAppRequestConnectRpcAsync) + ": Awaiting pending background connect from unlock");
                var pendingResult = await _pendingConnectTask;
                skippedConnect = true;
                if (pendingResult.IsSuccess) {
                    var stateResult = await _broker.EnqueueReadAsync(SignifyOperation.GetState, svc => svc.GetState());
                    connectResult = stateResult.IsSuccess
                        ? Result.Ok(stateResult.Value)
                        : Result.Fail<State>(stateResult.Errors);
                }
                else {
                    connectResult = Result.Fail<State>(pendingResult.Errors);
                }
            }
            else {
                logger.LogInformation(nameof(HandleAppRequestConnectRpcAsync) + ": Performing Connect (IsNewAgent={IsNew}, IsConnected={IsConn}, hasPending={HasPending})",
                    connectRequest.IsNewAgent, _signifyClientService.IsConnected, _pendingConnectTask is not null);

                // Cancel any active burst before connecting — Connect() resets the JS client,
                // and in-flight polling calls would hit "Client not connected"
                _notificationPollingCts?.Cancel();

                using (_broker.PrioritizeInteractive()) {
                    connectResult = await _broker.EnqueueCommandAsync(SignifyOperation.Connect,
                        svc => svc.Connect(
                            connectRequest.AdminUrl,
                            passcode,
                            connectRequest.BootUrl,
                            connectRequest.IsNewAgent,
                            connectRequest.BootAuthUsername,
                            connectRequest.BootAuthPassword
                        ));
                }
            }

            if (connectResult.IsSuccess && connectResult.Value is not null) {
                var clientAidPrefix = connectResult.Value.Controller?.State?.I;
                var agentAidPrefix = connectResult.Value.Agent?.I;

                // Only restart notification burst if we actually reconnected —
                // in the skip path, the burst from TryConnectSignifyClientAsync is already running.
                if (!skippedConnect) {
                    RestartNotificationBurst();
                }

                // Fetch identifiers and store in session for App to read
                var identifiersResult = await _broker.EnqueueReadAsync(SignifyOperation.GetIdentifiers,
                    svc => svc.GetIdentifiers());

                // Proactively cache credentials in session storage for App components to read directly
                await RefreshCachedCredentialsAsync();

                logger.LogInformation(nameof(HandleAppRequestConnectRpcAsync) + ": Connect succeeded. identifiersResult.IsSuccess={Success}",
                    identifiersResult.IsSuccess);

                if (identifiersResult.IsSuccess && identifiersResult.Value is not null) {
                    var connectionInfo = await _storageGateway.GetItem<KeriaConnectionInfo>(StorageArea.Session);
                    logger.LogInformation(nameof(HandleAppRequestConnectRpcAsync) + ": Existing KeriaConnectionInfo in session: IsSuccess={Success}, hasValue={HasValue}",
                        connectionInfo.IsSuccess, connectionInfo.Value is not null);

                    if (connectionInfo.IsSuccess && connectionInfo.Value is not null) {
                        // Update CachedIdentifiers + PollingState in one atomic write
                        var ps = await GetCurrentPollingStateAsync();
                        await _storageGateway.WriteTransaction(StorageArea.Session, tx => {
                            tx.SetItem(new CachedIdentifiers { IdentifiersList = [identifiersResult.Value] });
                            tx.SetItem(ps with { IdentifiersLastFetchedUtc = DateTime.UtcNow });
                            tx.SetItem(NextSessionSequence());
                        });
                        logger.LogInformation(nameof(HandleAppRequestConnectRpcAsync) + ": Updated CachedIdentifiers with identifiers");
                    }
                    else {
                        // KeriaConnectionInfo doesn't exist - create it using the digest computed from connect data
                        var tempConfig = new KeriaConnectConfig(
                            providerName: null,
                            adminUrl: connectRequest.AdminUrl,
                            bootUrl: connectRequest.BootUrl,
                            passcodeHash: connectRequest.PasscodeHash,
                            clientAidPrefix: clientAidPrefix,
                            agentAidPrefix: agentAidPrefix,
                            isStored: true
                        );
                        var digestResult = KeriaConnectionDigestHelper.Compute(tempConfig);
                        if (digestResult.IsFailed) {
                            logger.LogError(nameof(HandleAppRequestConnectRpcAsync) + ": Failed to compute KeriaConnectionDigest: {Errors}", string.Join(", ", digestResult.Errors));
                        }
                        else {
                            var newConnectionInfo = new KeriaConnectionInfo {
                                KeriaConnectionDigest = digestResult.Value
                            };
                            var ps = await GetCurrentPollingStateAsync();
                            await _storageGateway.WriteTransaction(StorageArea.Session, tx => {
                                tx.SetItem(newConnectionInfo);
                                tx.SetItem(new CachedIdentifiers { IdentifiersList = [identifiersResult.Value] });
                                tx.SetItem(ps with { IdentifiersLastFetchedUtc = DateTime.UtcNow });
                                tx.SetItem(NextSessionSequence());
                            });
                            logger.LogInformation(nameof(HandleAppRequestConnectRpcAsync) + ": Created KeriaConnectionInfo and CachedIdentifiers in session storage with digest: {Digest}", digestResult.Value);
                        }
                    }
                }
                else {
                    // Check if the failure is a 401 Unauthorized — indicates KERIA version mismatch
                    var errorMsg = identifiersResult.Errors.Count > 0 ? identifiersResult.Errors[0].Message : "";
                    if (errorMsg.Contains("401")) {
                        logger.LogError(nameof(HandleAppRequestConnectRpcAsync) + ": KERIA returned 401 after successful connect — likely version mismatch with {Url}", connectRequest.AdminUrl);
                        _notificationPollingCts?.Cancel();
                        await _storageGateway.RemoveItems(StorageArea.Session,
                            typeof(CachedIdentifiers), typeof(CachedCredentials), typeof(PollingState));
                        await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                            result: new ConnectResponsePayload(false,
                                Error: $"The KERIA service at {connectRequest.AdminUrl} returned 401 Unauthorized after authentication succeeded. This is usually caused by a version mismatch between this wallet and the KERIA deployment.",
                                ErrorCode: "keria_version_mismatch"));
                        return;
                    }
                    logger.LogWarning(nameof(HandleAppRequestConnectRpcAsync) + ": GetIdentifiers failed or returned null after connect");
                }

                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new ConnectResponsePayload(true, clientAidPrefix, agentAidPrefix));
            }
            else {
                var errorMsg = connectResult.Errors.Count > 0 ? connectResult.Errors[0].Message : "Connect failed";
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new ConnectResponsePayload(false, Error: errorMsg));
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppRequestConnectRpcAsync) + ": Error during connect");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new ConnectResponsePayload(false, Error: ex.Message));
        }
    }

    /// <summary>
    /// Handles get credentials request from App.
    /// Fetches credentials from KERIA via signify-ts and returns them.
    /// </summary>
    private async Task HandleAppRequestGetCredentialsRpcAsync(string portId, RpcRequest request) {
        logger.LogInformation(nameof(HandleAppRequestGetCredentialsRpcAsync) + ": called");

        if (!await RequireSignifyConnectionAsync(portId, request.PortSessionId, request.Id)) {
            return;
        }

        try {
            var credentialsResult = await _broker.EnqueueReadAsync(SignifyOperation.GetCredentials,
                svc => svc.GetCredentials());

            if (credentialsResult.IsSuccess) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new GetCredentialsResponsePayload(true, credentialsResult.Value));
            }
            else {
                var errorMsg = credentialsResult.Errors.Count > 0 ? credentialsResult.Errors[0].Message : "Get credentials failed";
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new GetCredentialsResponsePayload(false, Error: errorMsg));
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppRequestGetCredentialsRpcAsync) + ": Error during get credentials");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new GetCredentialsResponsePayload(false, Error: ex.Message));
        }
    }

    /// <summary>
    /// Fetches credentials from KERIA and writes raw JSON to session storage for App components to read directly.
    /// </summary>
    /// <summary>
    /// Refreshes the CachedCredentials session cache from KERIA.
    /// Skipped if:
    /// - Signify client is not connected
    /// - A long signify operation is in progress
    /// - Credentials were fetched within AppConfig.CredentialsPollSkipThreshold (unless force=true)
    /// </summary>
    // TODO P2: Add periodic polling for revocation status
    private async Task RefreshCachedCredentialsAsync(bool force = false) {
        if (!_signifyClientService.IsConnected) {
            logger.LogDebug(nameof(RefreshCachedCredentialsAsync) + ": Skipped — signify not connected");
            return;
        }
        if (!force && await IsWithinPollSkipThresholdAsync(
                ps => ps.CredentialsLastFetchedUtc, AppConfig.CredentialsPollSkipThreshold)) {
            logger.LogDebug(nameof(RefreshCachedCredentialsAsync) + ": Skipped — within poll threshold");
            return;
        }
        try {
            var rawJsonResult = await _broker.EnqueueBackgroundAsync(SignifyOperation.GetCredentialsRaw,
                svc => svc.GetCredentialsRaw());
            if (rawJsonResult.IsSuccess) {
                var credentialsDict = CredentialHelper.SplitCredentialsArrayToDict(rawJsonResult.Value);
                var pollingState = await GetCurrentPollingStateAsync();
                await _storageGateway.WriteTransaction(StorageArea.Session, tx => {
                    tx.SetItem(new CachedCredentials { Credentials = credentialsDict });
                    tx.SetItem(pollingState with { CredentialsLastFetchedUtc = DateTime.UtcNow });
                    tx.SetItem(NextSessionSequence());
                });
                logger.LogInformation(nameof(RefreshCachedCredentialsAsync) + ": Updated credentials in session storage");
            }
            else {
                logger.LogWarning(nameof(RefreshCachedCredentialsAsync) + ": Failed to fetch credentials: {Error}",
                    rawJsonResult.Errors.Count > 0 ? rawJsonResult.Errors[0].Message : "Unknown error");
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(RefreshCachedCredentialsAsync) + ": Error refreshing cached credentials");
        }
    }

    /// <summary>
    /// Wrapper around NotificationPollingService.PollOnDemandAsync that applies the
    /// NotificationsPollSkipThreshold to event-driven entry points. The burst loop inside
    /// NotificationPollingService continues to call PollOnDemandAsync directly (burst is self-timing).
    /// After a successful poll, NotificationPollingService updates NotificationsLastFetchedUtc.
    /// </summary>
    private async Task PollNotificationsThrottledAsync(bool force = false) {
        if (!force && await IsWithinPollSkipThresholdAsync(
                ps => ps.NotificationsLastFetchedUtc, AppConfig.NotificationsPollSkipThreshold)) {
            logger.LogDebug(nameof(PollNotificationsThrottledAsync) + ": Skipped — within poll threshold");
            return;
        }
        await _notificationPollingService.PollOnDemandAsync();
    }

    /// <summary>
    /// Helper: returns true if the selected PollingState timestamp is set and falls within the given threshold of now.
    /// Used by refresh methods to skip redundant fetches.
    /// </summary>
    private async Task<bool> IsWithinPollSkipThresholdAsync(Func<PollingState, DateTime?> selector, TimeSpan threshold) {
        try {
            var result = await _storageGateway.GetItem<PollingState>(StorageArea.Session);
            if (!result.IsSuccess || result.Value is null) return false;
            var timestamp = selector(result.Value);
            if (timestamp is null) return false;
            return DateTime.UtcNow - timestamp.Value < threshold;
        }
        catch {
            return false;
        }
    }

    /// <summary>
    /// Handles GetKeyState request from App.
    /// Returns the key state for a specific identifier prefix.
    /// </summary>
    private async Task HandleAppRequestGetKeyStateRpcAsync(string portId, RpcRequest request, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppRequestGetKeyStateRpcAsync) + ": called");

        if (!await RequireSignifyConnectionAsync(portId, request.PortSessionId, request.Id)) {
            return;
        }

        try {
            if (!payload.HasValue) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new GetKeyStateResponsePayload(false, Error: "Missing payload"));
                return;
            }

            var keyStateRequest = JsonSerializer.Deserialize<GetKeyStateRequestPayload>(
                payload.Value.GetRawText(), JsonOptions.CamelCase);

            if (keyStateRequest is null || string.IsNullOrEmpty(keyStateRequest.Prefix)) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new GetKeyStateResponsePayload(false, Error: "Invalid or missing prefix"));
                return;
            }

            var keyStateResult = await _broker.EnqueueReadAsync(SignifyOperation.GetKeyState,
                svc => svc.GetKeyState(keyStateRequest.Prefix));

            if (keyStateResult.IsSuccess) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new GetKeyStateResponsePayload(true, KeyState: keyStateResult.Value));
            }
            else {
                var errorMsg = keyStateResult.Errors.Count > 0 ? keyStateResult.Errors[0].Message : "Get key state failed";
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new GetKeyStateResponsePayload(false, Error: errorMsg));
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppRequestGetKeyStateRpcAsync) + ": Error during get key state");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new GetKeyStateResponsePayload(false, Error: ex.Message));
        }
    }

    /// <summary>
    /// Handles GetKeyEvents request from App.
    /// Returns the key events for a specific identifier prefix.
    /// </summary>
    private async Task HandleAppRequestGetKeyEventsRpcAsync(string portId, RpcRequest request, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppRequestGetKeyEventsRpcAsync) + ": called");

        if (!await RequireSignifyConnectionAsync(portId, request.PortSessionId, request.Id)) {
            return;
        }

        try {
            if (!payload.HasValue) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new GetKeyEventsResponsePayload(false, Error: "Missing payload"));
                return;
            }

            var keyEventsRequest = JsonSerializer.Deserialize<GetKeyEventsRequestPayload>(
                payload.Value.GetRawText(), JsonOptions.CamelCase);

            if (keyEventsRequest is null || string.IsNullOrEmpty(keyEventsRequest.Prefix)) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new GetKeyEventsResponsePayload(false, Error: "Invalid or missing prefix"));
                return;
            }

            var keyEventsResult = await _broker.EnqueueReadAsync(SignifyOperation.GetKeyEvents,
                svc => svc.GetKeyEvents(keyEventsRequest.Prefix));

            if (keyEventsResult.IsSuccess) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new GetKeyEventsResponsePayload(true, KeyEvents: keyEventsResult.Value));
            }
            else {
                var errorMsg = keyEventsResult.Errors.Count > 0 ? keyEventsResult.Errors[0].Message : "Get key events failed";
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new GetKeyEventsResponsePayload(false, Error: errorMsg));
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppRequestGetKeyEventsRpcAsync) + ": Error during get key events");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new GetKeyEventsResponsePayload(false, Error: ex.Message));
        }
    }

    /// <summary>
    /// Handles RenameAid request from App.
    /// Renames an AID and refreshes the identifiers cache.
    /// </summary>
    private async Task HandleAppRequestRenameAidRpcAsync(string portId, RpcRequest request, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppRequestRenameAidRpcAsync) + ": called");

        if (!await RequireSignifyConnectionAsync(portId, request.PortSessionId, request.Id)) {
            return;
        }

        try {
            if (!payload.HasValue) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new RenameAidResponsePayload(false, Error: "Missing payload"));
                return;
            }

            var renameRequest = JsonSerializer.Deserialize<RenameAidRequestPayload>(
                payload.Value.GetRawText(), JsonOptions.CamelCase);

            if (renameRequest is null || string.IsNullOrEmpty(renameRequest.CurrentName) || string.IsNullOrEmpty(renameRequest.NewName)) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new RenameAidResponsePayload(false, Error: "Invalid or missing current name or new name"));
                return;
            }

            var renameResult = await _broker.EnqueueCommandAsync(SignifyOperation.RenameAid,
                svc => svc.RenameAid(renameRequest.CurrentName, renameRequest.NewName));

            if (renameResult.IsSuccess) {
                // Refresh the identifiers cache after successful rename
                await RefreshIdentifiersCache();

                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new RenameAidResponsePayload(true));
            }
            else {
                var errorMsg = renameResult.Errors.Count > 0 ? renameResult.Errors[0].Message : "Rename AID failed";
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new RenameAidResponsePayload(false, Error: errorMsg));
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppRequestRenameAidRpcAsync) + ": Error during rename AID");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new RenameAidResponsePayload(false, Error: ex.Message));
        }
    }

    private async Task HandleAppRequestConfigureRpcAsync(string portId, RpcRequest request, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppRequestConfigureRpcAsync) + ": called");

        try {
            if (!payload.HasValue) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new ConfigureResponsePayload(false, Error: "Missing payload"));
                return;
            }

            var configurePayload = JsonSerializer.Deserialize<ConfigureRequestPayload>(
                payload.Value.GetRawText(), JsonOptions.CamelCase);

            if (configurePayload is null || string.IsNullOrEmpty(configurePayload.AdminUrl)) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new ConfigureResponsePayload(false, Error: "Invalid configure parameters"));
                return;
            }

            // Cancel any active notification polling before connecting
            _notificationPollingCts?.Cancel();

            var result = await _configureService.ConfigureAsync(configurePayload);

            if (result.IsSuccess && result.Value.Success) {
                RestartNotificationBurst();
                await RefreshCachedCredentialsAsync();
                await RefreshIdentifiersCache();
            }

            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: result.IsSuccess ? result.Value : new ConfigureResponsePayload(false, Error: "Configure failed"));
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppRequestConfigureRpcAsync) + ": Error during configure");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new ConfigureResponsePayload(false, Error: ex.Message));
        }
    }

    private async Task HandleAppRequestResetConfigureRpcAsync(string portId, RpcRequest request) {
        logger.LogInformation(nameof(HandleAppRequestResetConfigureRpcAsync) + ": called");

        try {
            var result = await _configureService.ResetAsync();
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new { success = result.IsSuccess });
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppRequestResetConfigureRpcAsync) + ": Error during reset");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: ex.Message);
        }
    }

    private async Task HandleAppRequestPrimeDataGoRpcAsync(string portId, RpcRequest request, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppRequestPrimeDataGoRpcAsync) + ": called");

        if (!await RequireSignifyConnectionAsync(portId, request.PortSessionId, request.Id)) {
            return;
        }

        // Acknowledge immediately — progress and completion are reported via session storage
        await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
            result: new { acknowledged = true });

        // Run the long operation in the background (not tied to RPC timeout)
        _ = Task.Run(async () => {
            try {
                var goPayload = payload.HasValue
                    ? JsonSerializer.Deserialize<PrimeDataGoPayload>(payload.Value.GetRawText(), JsonOptions.CamelCase)
                    : null;

                await _primeDataService.GoAsync(goPayload);
            }
            catch (Exception ex) {
                logger.LogError(ex, nameof(HandleAppRequestPrimeDataGoRpcAsync) + ": Unhandled error during PrimeData Go");
            }
            finally {
                // Refresh identifiers cache since AIDs may have been created (even on partial failure)
                await RefreshIdentifiersCache();
            }
        });
    }

    private async Task HandleAppRequestPrimeDataIpexRpcAsync(string portId, RpcRequest request, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppRequestPrimeDataIpexRpcAsync) + ": called");

        if (!await RequireSignifyConnectionAsync(portId, request.PortSessionId, request.Id)) {
            return;
        }

        // Validate payload synchronously before acknowledging
        if (!payload.HasValue) {
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: "Missing payload");
            return;
        }

        var ipexPayload = JsonSerializer.Deserialize<PrimeDataIpexPayload>(
            payload.Value.GetRawText(), JsonOptions.CamelCase);

        if (ipexPayload is null || string.IsNullOrEmpty(ipexPayload.DiscloserPrefix) || string.IsNullOrEmpty(ipexPayload.DiscloseePrefix)) {
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: "Invalid or missing payload fields");
            return;
        }

        // Acknowledge immediately — progress and completion are reported via session storage
        await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
            result: new { acknowledged = true });

        // Run the long operation in the background (not tied to RPC timeout)
        _ = Task.Run(async () => {
            try {
                await _primeDataService.GoIpexAsync(ipexPayload);
            }
            catch (Exception ex) {
                logger.LogError(ex, nameof(HandleAppRequestPrimeDataIpexRpcAsync) + ": Unhandled error during PrimeData IPEX");
            }
            finally {
                await RefreshIdentifiersCache();
            }
        });
    }

    private async Task HandleAppRequestIpexEligibleDisclosersRpcAsync(string portId, RpcRequest request, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppRequestIpexEligibleDisclosersRpcAsync) + ": called");

        if (!await RequireSignifyConnectionAsync(portId, request.PortSessionId, request.Id)) {
            return;
        }

        try {
            if (!payload.HasValue) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexEligibleDisclosersResponse(false, [], Error: "Missing payload"));
                return;
            }

            var eligiblePayload = JsonSerializer.Deserialize<IpexEligibleDisclosersPayload>(
                payload.Value.GetRawText(), JsonOptions.CamelCase);

            if (eligiblePayload is null) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexEligibleDisclosersResponse(false, [], Error: "Invalid payload"));
                return;
            }

            var result = await _primeDataService.GetEligibleDiscloserPrefixes(eligiblePayload.IsPresentation, eligiblePayload.Workflow);

            if (result.IsSuccess) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexEligibleDisclosersResponse(true, result.Value));
            }
            else {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexEligibleDisclosersResponse(false, [], Error: result.Errors[0].Message));
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppRequestIpexEligibleDisclosersRpcAsync) + ": Error");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new IpexEligibleDisclosersResponse(false, [], Error: ex.Message));
        }
    }

    private async Task HandleAppRequestGetOobiRpcAsync(string portId, RpcRequest request, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppRequestGetOobiRpcAsync) + ": called");

        if (!await RequireSignifyConnectionAsync(portId, request.PortSessionId, request.Id)) {
            return;
        }

        try {
            if (!payload.HasValue) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new GetOobiResponse(false, Error: "Missing payload"));
                return;
            }

            var getOobiRequest = JsonSerializer.Deserialize<RequestGetOobiPayload>(
                payload.Value.GetRawText(), JsonOptions.CamelCase);

            if (getOobiRequest is null || string.IsNullOrEmpty(getOobiRequest.AidName)) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new GetOobiResponse(false, Error: "Invalid or missing AID name"));
                return;
            }

            var oobiResult = await _broker.EnqueueReadAsync(SignifyOperation.GetOobi,
                svc => svc.GetOobi(getOobiRequest.AidName, "agent"));

            if (oobiResult.IsSuccess && oobiResult.Value is not null) {
                // The result contains an "oobis" field with an array of OOBI URLs
                string? oobiUrl = null;
                if (oobiResult.Value.TryGetValue("oobis", out var oobisVal)) {
                    // oobis is an array — take the first element
                    oobiUrl = oobisVal?.List?.FirstOrDefault()?.StringValue
                              ?? oobisVal?.StringValue;
                }
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new GetOobiResponse(true, Oobi: oobiUrl));
            }
            else {
                var errorMsg = oobiResult.Errors.Count > 0 ? oobiResult.Errors[0].Message : "Failed to get OOBI";
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new GetOobiResponse(false, Error: errorMsg));
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppRequestGetOobiRpcAsync) + ": Error getting OOBI");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new GetOobiResponse(false, Error: ex.Message));
        }
    }

    private async Task HandleAppRequestResolveOobiRpcAsync(string portId, RpcRequest request, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppRequestResolveOobiRpcAsync) + ": called");

        if (!await RequireSignifyConnectionAsync(portId, request.PortSessionId, request.Id)) {
            return;
        }

        try {
            if (!payload.HasValue) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new ResolveOobiResponse(false, Error: "Missing payload"));
                return;
            }

            var resolveRequest = JsonSerializer.Deserialize<RequestResolveOobiPayload>(
                payload.Value.GetRawText(), JsonOptions.CamelCase);

            if (resolveRequest is null || string.IsNullOrEmpty(resolveRequest.OobiUrl)) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new ResolveOobiResponse(false, Error: "Invalid or missing OOBI URL"));
                return;
            }

            var resolveResult = await _broker.EnqueueCommandAsync(SignifyOperation.ResolveOobi,
                svc => svc.ResolveOobi(resolveRequest.OobiUrl, resolveRequest.Alias));

            if (resolveResult.IsSuccess) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new ResolveOobiResponse(true));
            }
            else {
                var errorMsg = resolveResult.Errors.Count > 0 ? resolveResult.Errors[0].Message : "Failed to resolve OOBI";
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new ResolveOobiResponse(false, Error: errorMsg));
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppRequestResolveOobiRpcAsync) + ": Error resolving OOBI");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new ResolveOobiResponse(false, Error: ex.Message));
        }
    }

    private async Task HandleAppRequestGetExchangeRpcAsync(string portId, RpcRequest request, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppRequestGetExchangeRpcAsync) + ": called");

        if (!await RequireSignifyConnectionAsync(portId, request.PortSessionId, request.Id)) {
            return;
        }

        try {
            if (!payload.HasValue) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new GetExchangeResponsePayload(false, Error: "Missing payload"));
                return;
            }

            var exchangeRequest = JsonSerializer.Deserialize<GetExchangeRequestPayload>(
                payload.Value.GetRawText(), JsonOptions.CamelCase);

            if (exchangeRequest is null || string.IsNullOrEmpty(exchangeRequest.Said)) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new GetExchangeResponsePayload(false, Error: "Invalid or missing exchange SAID"));
                return;
            }

            var exchangeResult = await GetExchangeCachedAsync(exchangeRequest.Said);

            if (exchangeResult.IsSuccess) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new GetExchangeResponsePayload(true, Exchange: exchangeResult.Value));
            }
            else {
                var errorMsg = exchangeResult.Errors.Count > 0 ? exchangeResult.Errors[0].Message : "Get exchange failed";
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new GetExchangeResponsePayload(false, Error: errorMsg));
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppRequestGetExchangeRpcAsync) + ": Error getting exchange");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new GetExchangeResponsePayload(false, Error: ex.Message));
        }
    }

    private async Task HandleAppRequestIpexAdmitRpcAsync(string portId, RpcRequest request, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppRequestIpexAdmitRpcAsync) + ": called");

        if (!await RequireSignifyConnectionAsync(portId, request.PortSessionId, request.Id)) {
            return;
        }

        try {
            if (!payload.HasValue) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexAdmitResponsePayload(false, Error: "Missing payload"));
                return;
            }

            var admitRequest = JsonSerializer.Deserialize<IpexAdmitRequestPayload>(
                payload.Value.GetRawText(), JsonOptions.CamelCase);

            if (admitRequest is null || string.IsNullOrEmpty(admitRequest.SenderNameOrPrefix)
                || string.IsNullOrEmpty(admitRequest.RecipientPrefix) || string.IsNullOrEmpty(admitRequest.GrantSaid)) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexAdmitResponsePayload(false, Error: "Invalid or missing admit parameters"));
                return;
            }

            // Note: schema OOBIs should already be resolved proactively by notification polling (OnSchemasNeeded callback)

            logger.LogInformation(nameof(HandleAppRequestIpexAdmitRpcAsync) +
                ": Starting IpexAdmitAndSubmit, sender={Sender}, recipient={Recipient}, grantSaid={GrantSaid}",
                admitRequest.SenderNameOrPrefix, admitRequest.RecipientPrefix, admitRequest.GrantSaid);

            var result = await _primeDataService.AdmitStep(new IpexAdmitSubmitArgs(
                SenderNameOrPrefix: admitRequest.SenderNameOrPrefix,
                RecipientPrefix: admitRequest.RecipientPrefix,
                GrantSaid: admitRequest.GrantSaid
            ), nameof(HandleAppRequestIpexAdmitRpcAsync));

            if (result.IsSuccess) {
                await PollNotificationsThrottledAsync();
                await RefreshCachedCredentialsAsync();
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexAdmitResponsePayload(true));
            }
            else {
                var errorMsg = result.Errors.Count > 0 ? result.Errors[0].Message : "IPEX admit failed";
                logger.LogWarning(nameof(HandleAppRequestIpexAdmitRpcAsync) + ": {Error}", errorMsg);
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexAdmitResponsePayload(false, Error: errorMsg));
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppRequestIpexAdmitRpcAsync) + ": Error during IPEX admit");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new IpexAdmitResponsePayload(false, Error: ex.Message));
        }
    }

    private async Task HandleAppRequestIpexAgreeRpcAsync(string portId, RpcRequest request, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppRequestIpexAgreeRpcAsync) + ": called");

        if (!await RequireSignifyConnectionAsync(portId, request.PortSessionId, request.Id)) {
            return;
        }

        try {
            if (!payload.HasValue) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexAgreeResponsePayload(false, Error: "Missing payload"));
                return;
            }

            var agreeRequest = JsonSerializer.Deserialize<IpexAgreeRequestPayload>(
                payload.Value.GetRawText(), JsonOptions.CamelCase);

            if (agreeRequest is null || string.IsNullOrEmpty(agreeRequest.SenderNameOrPrefix)
                || string.IsNullOrEmpty(agreeRequest.RecipientPrefix) || string.IsNullOrEmpty(agreeRequest.OfferSaid)) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexAgreeResponsePayload(false, Error: "Invalid or missing agree parameters"));
                return;
            }

            // Note: schema OOBIs should already be resolved proactively by notification polling (OnSchemasNeeded callback)

            logger.LogInformation(nameof(HandleAppRequestIpexAgreeRpcAsync) +
                ": sender={Sender}, recipient={Recipient}, offerSaid={OfferSaid}",
                agreeRequest.SenderNameOrPrefix, agreeRequest.RecipientPrefix, agreeRequest.OfferSaid);

            var result = await _primeDataService.AgreeStep(new IpexAgreeSubmitArgs(
                SenderNameOrPrefix: agreeRequest.SenderNameOrPrefix,
                RecipientPrefix: agreeRequest.RecipientPrefix,
                OfferSaid: agreeRequest.OfferSaid
            ), nameof(HandleAppRequestIpexAgreeRpcAsync));

            if (result.IsSuccess) {
                await PollNotificationsThrottledAsync();
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexAgreeResponsePayload(true));
            }
            else {
                var errorMsg = result.Errors.Count > 0 ? result.Errors[0].Message : "IPEX agree failed";
                logger.LogWarning(nameof(HandleAppRequestIpexAgreeRpcAsync) + ": {Error}", errorMsg);
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexAgreeResponsePayload(false, Error: errorMsg));
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppRequestIpexAgreeRpcAsync) + ": Error during IPEX agree");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new IpexAgreeResponsePayload(false, Error: ex.Message));
        }
    }

    private async Task HandleAppRequestIpexOfferRpcAsync(string portId, RpcRequest request, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppRequestIpexOfferRpcAsync) + ": called");

        if (!await RequireSignifyConnectionAsync(portId, request.PortSessionId, request.Id)) {
            return;
        }

        try {
            if (!payload.HasValue) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexOfferResponsePayload(false, Error: "Missing payload"));
                return;
            }

            var offerRequest = JsonSerializer.Deserialize<IpexOfferRequestPayload>(
                payload.Value.GetRawText(), JsonOptions.CamelCase);

            if (offerRequest is null || string.IsNullOrEmpty(offerRequest.SenderNameOrPrefix)
                || string.IsNullOrEmpty(offerRequest.RecipientPrefix)
                || string.IsNullOrEmpty(offerRequest.ApplySaid)
                || string.IsNullOrEmpty(offerRequest.EcrRole)) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexOfferResponsePayload(false, Error: "Invalid or missing offer parameters"));
                return;
            }

            var senderPrefix = offerRequest.SenderNameOrPrefix;
            var recipientPrefix = offerRequest.RecipientPrefix;

            // Resolve sender AID name
            var senderNameResult = await GetIdentifierNameFromCacheAsync(senderPrefix);
            if (senderNameResult.IsFailed) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexOfferResponsePayload(false, Error: $"Could not resolve AID name for {senderPrefix}"));
                return;
            }
            var senderName = senderNameResult.Value;

            logger.LogInformation(nameof(HandleAppRequestIpexOfferRpcAsync) +
                ": sender={Sender} ({SenderName}), recipient={Recipient}, applySaid={ApplySaid}, role={Role}",
                senderPrefix, senderName, recipientPrefix, offerRequest.ApplySaid, offerRequest.EcrRole);

            // Ensure ECR schema is resolved
            if (!await EnsureSchemaResolvedAsync(VleiCredentialHelper.EcrSchemaSaid, nameof(HandleAppRequestIpexOfferRpcAsync))) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexOfferResponsePayload(false, Error: "Failed to resolve ECR schema"));
                return;
            }

            using (_broker.PrioritizeInteractive()) {
                // Look up ECR Auth credential held by sender
                var credsResult = await _broker.EnqueueCommandAsync(SignifyOperation.GetCredentials,
                    svc => svc.GetCredentials());
                if (credsResult.IsFailed) {
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                        result: new IpexOfferResponsePayload(false, Error: $"Failed to get credentials: {credsResult.Errors[0].Message}"));
                    return;
                }

                var ecrAuthCred = VleiCredentialHelper.FindEcrAuthCredential(credsResult.Value, senderPrefix);

                if (ecrAuthCred is null) {
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                        result: new IpexOfferResponsePayload(false, Error: $"Sender {senderPrefix} does not hold an ECR Auth credential"));
                    return;
                }
                var ecrAuthSaid = ecrAuthCred.GetValueByPath("sad.d")?.Value?.ToString()!;
                logger.LogInformation(nameof(HandleAppRequestIpexOfferRpcAsync) + ": Found ECR Auth credential: said={Said}", ecrAuthSaid);

                // Create registry
                var registryName = $"{senderName}_ecr_offer_registry";
                var registryResult = await _broker.EnqueueCommandAsync(SignifyOperation.CreateRegistryIfNotExists,
                    svc => svc.CreateRegistryIfNotExists(senderName, registryName));
                if (registryResult.IsFailed) {
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                        result: new IpexOfferResponsePayload(false, Error: $"Failed to create registry: {registryResult.Errors[0].Message}"));
                    return;
                }

                // Issue ECR credential
                var ecrCredData = VleiCredentialHelper.BuildEcrCredentialData(offerRequest.EcrRole);
                var ecrEdge = VleiCredentialHelper.BuildEcrAuthEdge(ecrAuthSaid);
                var ecrRules = VleiCredentialHelper.BuildVleiRules(VleiCredentialHelper.EcrPrivacyDisclaimer);

                logger.LogInformation(nameof(HandleAppRequestIpexOfferRpcAsync) + ": Issuing ECR credential...");
                var issueResult = await _broker.EnqueueCommandAsync(SignifyOperation.IssueAndGetCredential,
                    svc => svc.IssueAndGetCredential(new IssueAndGetCredentialArgs(
                    IssuerAidNameOrPrefix: senderName,
                    RegistryName: registryName,
                    Schema: VleiCredentialHelper.EcrSchemaSaid,
                    HolderPrefix: recipientPrefix,
                    CredData: ecrCredData,
                    CredEdge: ecrEdge,
                    CredRules: ecrRules,
                    Private: true
                )));

                if (issueResult.IsFailed) {
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                        result: new IpexOfferResponsePayload(false, Error: $"Failed to issue credential: {issueResult.Errors[0].Message}"));
                    return;
                }

                var credentialSaid = issueResult.Value["said"]?.StringValue;
                if (credentialSaid is null) {
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                        result: new IpexOfferResponsePayload(false, Error: "Issued credential has no SAID"));
                    return;
                }
                logger.LogInformation(nameof(HandleAppRequestIpexOfferRpcAsync) + ": ECR credential issued: said={Said}", credentialSaid);

                // Send IPEX Offer
                logger.LogInformation(nameof(HandleAppRequestIpexOfferRpcAsync) + ": Sending IPEX offer...");
                var offerResult = await _broker.EnqueueCommandAsync(SignifyOperation.IpexOfferAndSubmit,
                    svc => svc.IpexOfferAndSubmit(new IpexOfferSubmitArgs(
                        SenderNameOrPrefix: senderPrefix,
                        RecipientPrefix: recipientPrefix,
                        CredentialSaid: credentialSaid,
                        ApplySaid: offerRequest.ApplySaid
                    )));

                if (offerResult.IsSuccess) {
                    var offerSaid = offerResult.Value["offerSaid"]?.StringValue;
                    logger.LogInformation(nameof(HandleAppRequestIpexOfferRpcAsync) + ": Offer submitted: offerSaid={OfferSaid}", offerSaid);
                    await PollNotificationsThrottledAsync();
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                        result: new IpexOfferResponsePayload(true));
                }
                else {
                    var errorMsg = offerResult.Errors.Count > 0 ? offerResult.Errors[0].Message : "IPEX offer failed";
                    logger.LogWarning(nameof(HandleAppRequestIpexOfferRpcAsync) + ": {Error}", errorMsg);
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                        result: new IpexOfferResponsePayload(false, Error: errorMsg));
                }
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppRequestIpexOfferRpcAsync) + ": Error during IPEX offer");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new IpexOfferResponsePayload(false, Error: ex.Message));
        }
    }

    private async Task HandleAppRequestIpexGrantRpcAsync(string portId, RpcRequest request, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppRequestIpexGrantRpcAsync) + ": called");

        if (!await RequireSignifyConnectionAsync(portId, request.PortSessionId, request.Id)) {
            return;
        }

        try {
            if (!payload.HasValue) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexGrantResponsePayload(false, Error: "Missing payload"));
                return;
            }

            var grantRequest = JsonSerializer.Deserialize<IpexGrantRequestPayload>(
                payload.Value.GetRawText(), JsonOptions.CamelCase);

            if (grantRequest is null || string.IsNullOrEmpty(grantRequest.SenderNameOrPrefix)
                || string.IsNullOrEmpty(grantRequest.RecipientPrefix)) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexGrantResponsePayload(false, Error: "Invalid or missing grant parameters"));
                return;
            }

            if (!string.IsNullOrEmpty(grantRequest.ApplySaid)) {
                await HandleGrantFromApplyAsync(portId, request, grantRequest);
            }
            else if (!string.IsNullOrEmpty(grantRequest.AgreeSaid)) {
                await HandleGrantFromAgreeAsync(portId, request, grantRequest);
            }
            else {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexGrantResponsePayload(false, Error: "Either applySaid or agreeSaid must be provided"));
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppRequestIpexGrantRpcAsync) + ": Error during IPEX grant");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new IpexGrantResponsePayload(false, Error: ex.Message));
        }
    }

    /// <summary>
    /// Grant a newly issued credential in response to an apply (abbreviated apply→grant flow).
    /// Issues ECR credential using sender's ECR Auth, then grants it to the applicant.
    /// </summary>
    private async Task HandleGrantFromApplyAsync(string portId, RpcRequest request, IpexGrantRequestPayload grantRequest) {
        var senderPrefix = grantRequest.SenderNameOrPrefix;
        var recipientPrefix = grantRequest.RecipientPrefix;

        if (string.IsNullOrEmpty(grantRequest.EcrRole)) {
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new IpexGrantResponsePayload(false, Error: "ecrRole is required for grant from apply"));
            return;
        }

        var senderNameResult = await GetIdentifierNameFromCacheAsync(senderPrefix);
        if (senderNameResult.IsFailed) {
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new IpexGrantResponsePayload(false, Error: $"Could not resolve AID name for {senderPrefix}"));
            return;
        }
        var senderName = senderNameResult.Value;

        logger.LogInformation(nameof(HandleGrantFromApplyAsync) +
            ": sender={Sender} ({SenderName}), recipient={Recipient}, applySaid={ApplySaid}, role={Role}",
            senderPrefix, senderName, recipientPrefix, grantRequest.ApplySaid, grantRequest.EcrRole);

        if (!await EnsureSchemaResolvedAsync(VleiCredentialHelper.EcrSchemaSaid, nameof(HandleGrantFromApplyAsync))) {
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new IpexGrantResponsePayload(false, Error: "Failed to resolve ECR schema"));
            return;
        }

        using (_broker.PrioritizeInteractive()) {
            // Look up ECR Auth credential held by sender
            var credsResult = await _broker.EnqueueCommandAsync(SignifyOperation.GetCredentials,
                svc => svc.GetCredentials());
            if (credsResult.IsFailed) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexGrantResponsePayload(false, Error: $"Failed to get credentials: {credsResult.Errors[0].Message}"));
                return;
            }

            var ecrAuthCred = VleiCredentialHelper.FindEcrAuthCredential(credsResult.Value, senderPrefix);
            if (ecrAuthCred is null) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexGrantResponsePayload(false, Error: $"Sender {senderPrefix} does not hold an ECR Auth credential"));
                return;
            }
            var ecrAuthSaid = ecrAuthCred.GetValueByPath("sad.d")?.Value?.ToString()!;
            logger.LogInformation(nameof(HandleGrantFromApplyAsync) + ": Found ECR Auth credential: said={Said}", ecrAuthSaid);

            // Create registry
            var registryName = $"{senderName}_ecr_grant_registry";
            var registryResult = await _broker.EnqueueCommandAsync(SignifyOperation.CreateRegistryIfNotExists,
                svc => svc.CreateRegistryIfNotExists(senderName, registryName));
            if (registryResult.IsFailed) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexGrantResponsePayload(false, Error: $"Failed to create registry: {registryResult.Errors[0].Message}"));
                return;
            }

            // Issue ECR credential
            var ecrCredData = VleiCredentialHelper.BuildEcrCredentialData(grantRequest.EcrRole);
            var ecrEdge = VleiCredentialHelper.BuildEcrAuthEdge(ecrAuthSaid);
            var ecrRules = VleiCredentialHelper.BuildVleiRules(VleiCredentialHelper.EcrPrivacyDisclaimer);

            logger.LogInformation(nameof(HandleGrantFromApplyAsync) + ": Issuing ECR credential...");
            var issueResult = await _broker.EnqueueCommandAsync(SignifyOperation.IssueAndGetCredential,
                svc => svc.IssueAndGetCredential(new IssueAndGetCredentialArgs(
                IssuerAidNameOrPrefix: senderName,
                RegistryName: registryName,
                Schema: VleiCredentialHelper.EcrSchemaSaid,
                HolderPrefix: recipientPrefix,
                CredData: ecrCredData,
                CredEdge: ecrEdge,
                CredRules: ecrRules,
                Private: true
            )));

            if (issueResult.IsFailed) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexGrantResponsePayload(false, Error: $"Failed to issue credential: {issueResult.Errors[0].Message}"));
                return;
            }

            var acdc = issueResult.Value["acdc"]?.Dictionary;
            var anc = issueResult.Value["anc"]?.Dictionary;
            var iss = issueResult.Value["iss"]?.Dictionary;
            var credSaid = issueResult.Value["said"]?.StringValue;

            if (acdc is null || anc is null || iss is null || credSaid is null) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexGrantResponsePayload(false, Error: "Issued credential missing required fields"));
                return;
            }
            logger.LogInformation(nameof(HandleGrantFromApplyAsync) + ": ECR credential issued: said={Said}", credSaid);

            // Grant the credential
            logger.LogInformation(nameof(HandleGrantFromApplyAsync) + ": Sending IPEX grant...");
            var grantResult = await _primeDataService.GrantStep(new IpexGrantSubmitArgs(
                SenderNameOrPrefix: senderPrefix,
                RecipientPrefix: recipientPrefix,
                Acdc: acdc,
                Anc: anc,
                Iss: iss
            ), nameof(HandleGrantFromApplyAsync));

            if (grantResult.IsSuccess) {
                logger.LogInformation(nameof(HandleGrantFromApplyAsync) + ": Grant submitted: grantSaid={GrantSaid}", grantResult.Value);
                await PollNotificationsThrottledAsync();
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexGrantResponsePayload(true));
            }
            else {
                var errorMsg = grantResult.Errors.Count > 0 ? grantResult.Errors[0].Message : "IPEX grant failed";
                logger.LogWarning(nameof(HandleGrantFromApplyAsync) + ": {Error}", errorMsg);
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexGrantResponsePayload(false, Error: errorMsg));
            }
        }
    }

    /// <summary>
    /// Grant (present) an existing credential in response to an agree (agree→grant flow).
    /// Traces agree→offer→credential, then presents the credential.
    /// </summary>
    private async Task HandleGrantFromAgreeAsync(string portId, RpcRequest request, IpexGrantRequestPayload grantRequest) {
        var senderPrefix = grantRequest.SenderNameOrPrefix;
        var recipientPrefix = grantRequest.RecipientPrefix;

        var senderNameResult = await GetIdentifierNameFromCacheAsync(senderPrefix);
        if (senderNameResult.IsFailed) {
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new IpexGrantResponsePayload(false, Error: $"Could not resolve AID name for {senderPrefix}"));
            return;
        }
        var senderName = senderNameResult.Value;

        logger.LogInformation(nameof(HandleGrantFromAgreeAsync) +
            ": sender={Sender} ({SenderName}), recipient={Recipient}, agreeSaid={AgreeSaid}",
            senderPrefix, senderName, recipientPrefix, grantRequest.AgreeSaid);

        // Get the agree exchange to find the prior offer SAID
        var agreeExchangeResult = await GetExchangeCachedAsync(grantRequest.AgreeSaid!);
        if (agreeExchangeResult.IsFailed) {
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new IpexGrantResponsePayload(false, Error: $"Failed to get agree exchange: {agreeExchangeResult.Errors[0].Message}"));
            return;
        }

        var agreeView = ExchangeView.FromRecursiveDictionary(agreeExchangeResult.Value);
        var offerSaid = agreeView.P;
        if (string.IsNullOrEmpty(offerSaid)) {
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new IpexGrantResponsePayload(false, Error: "Agree exchange has no prior offer reference"));
            return;
        }

        // Get the prior offer exchange to find the credential SAID
        var offerExchangeResult = await GetExchangeCachedAsync(offerSaid);
        if (offerExchangeResult.IsFailed) {
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new IpexGrantResponsePayload(false, Error: $"Failed to get prior offer exchange: {offerExchangeResult.Errors[0].Message}"));
            return;
        }

        var offerView = ExchangeView.FromRecursiveDictionary(offerExchangeResult.Value);
        var credentialSaid = offerView.E?.GetValueByPath("acdc.d")?.Value?.ToString();
        if (string.IsNullOrEmpty(credentialSaid)) {
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new IpexGrantResponsePayload(false, Error: "Could not find credential SAID in prior offer"));
            return;
        }

        logger.LogInformation(nameof(HandleGrantFromAgreeAsync) +
            ": Found credential SAID={CredSaid} from offer={OfferSaid}", credentialSaid, offerSaid);

        var grantResult = await _primeDataService.PresentStep(
            senderName, credentialSaid, recipientPrefix, nameof(HandleGrantFromAgreeAsync));

        if (grantResult.IsSuccess) {
            await PollNotificationsThrottledAsync();
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new IpexGrantResponsePayload(true));
        }
        else {
            var errorMsg = grantResult.Errors.Count > 0 ? grantResult.Errors[0].Message : "IPEX grant failed";
            logger.LogWarning(nameof(HandleGrantFromAgreeAsync) + ": {Error}", errorMsg);
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new IpexGrantResponsePayload(false, Error: errorMsg));
        }
    }

    /// <summary>
    /// Offer or grant a held credential in response to a presentation request.
    /// Offer path (apply → offer): sends IPEX offer with held credential, awaiting agree before grant.
    /// Grant path (apply → grant): grants held credential directly.
    /// The user selected a credential and configured disclosure in the OfferOrOfferOrGrantPresentationDialog.
    /// </summary>
    private async Task HandleAppRequestIpexOfferOrGrantPresentationRpcAsync(string portId, RpcRequest request, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppRequestIpexOfferOrGrantPresentationRpcAsync) + ": called");

        if (!await RequireSignifyConnectionAsync(portId, request.PortSessionId, request.Id)) {
            return;
        }

        try {
            if (!payload.HasValue) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexGrantResponsePayload(false, Error: "Missing payload"));
                return;
            }

            var grantRequest = JsonSerializer.Deserialize<IpexGrantPresentationRequestPayload>(
                payload.Value.GetRawText(), JsonOptions.CamelCase);

            if (grantRequest is null || string.IsNullOrEmpty(grantRequest.SenderNameOrPrefix)
                || string.IsNullOrEmpty(grantRequest.RecipientPrefix)
                || string.IsNullOrEmpty(grantRequest.CredentialSaid)) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexGrantResponsePayload(false, Error: "Invalid or missing grant presentation parameters"));
                return;
            }

            var senderNameResult = await GetIdentifierNameFromCacheAsync(grantRequest.SenderNameOrPrefix);
            if (senderNameResult.IsFailed) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexGrantResponsePayload(false, Error: $"Could not resolve AID name for {grantRequest.SenderNameOrPrefix}"));
                return;
            }
            var senderName = senderNameResult.Value;

            // Check if selective disclosure is requested
            var isSelectiveDisclosure = grantRequest.ElisionMap is not null
                && grantRequest.ElisionMap.Any(kv => !kv.Value);

            var actionLabel = grantRequest.IsOffer ? "offer" : "grant";

            logger.LogInformation(nameof(HandleAppRequestIpexOfferOrGrantPresentationRpcAsync) +
                ": {Disclosure} {Action} — sender={Sender} ({SenderName}), recipient={Recipient}, credSaid={CredSaid}",
                isSelectiveDisclosure ? "Selective disclosure" : "Full disclosure",
                actionLabel, grantRequest.SenderNameOrPrefix, senderName, grantRequest.RecipientPrefix, grantRequest.CredentialSaid);

            Result<string> result;

            switch (isSelectiveDisclosure, grantRequest.IsOffer) {
                case (true, true):
                    // TODO P1 Selective disclosure + offer path: not yet designed
                    await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                        result: new IpexGrantResponsePayload(false, Error: "Selective disclosure with offer is not yet implemented."));
                    return;

                case (true, false): {
                    // Selective disclosure grant path: elide ACDC, saidify in signify-ts, grant
                    var credResult = await _broker.EnqueueCommandAsync(SignifyOperation.GetCredentials,
                        svc => svc.GetCredentials());
                    if (credResult.IsFailed) {
                        await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                            result: new IpexGrantResponsePayload(false, Error: $"Failed to get credentials: {credResult.Errors[0].Message}"));
                        return;
                    }

                    var credential = credResult.Value.FirstOrDefault(c =>
                        c.GetValueByPath("sad.d")?.Value?.ToString() == grantRequest.CredentialSaid);
                    if (credential is null) {
                        await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                            result: new IpexGrantResponsePayload(false, Error: $"Credential {grantRequest.CredentialSaid} not found"));
                        return;
                    }

                    var sad = credential.GetByPath("sad")?.Dictionary;
                    if (sad is null) {
                        await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                            result: new IpexGrantResponsePayload(false, Error: "Credential has no SAD"));
                        return;
                    }

                    var elidedAcdc = Helper.CredentialHelper.ElideAcdc(sad, grantRequest.ElisionMap!);
                    var elidedAcdcJson = JsonSerializer.Serialize(elidedAcdc.ToObjectDictionary(), JsonOptions.CamelCase);

                    logger.LogInformation(nameof(HandleAppRequestIpexOfferOrGrantPresentationRpcAsync) +
                        ": Elided ACDC prepared, calling grantWithElidedAcdc");

                    var grantResult = await _broker.EnqueueCommandAsync(SignifyOperation.GrantWithElidedAcdc,
                        svc => svc.GrantWithElidedAcdc(
                            senderName, elidedAcdcJson, grantRequest.CredentialSaid, grantRequest.RecipientPrefix));

                    result = grantResult.IsSuccess
                        ? Result.Ok(grantResult.Value["grantSaid"]?.StringValue ?? "")
                        : Result.Fail<string>(grantResult.Errors);
                    break;
                }

                case (false, true): {
                    // Full disclosure offer path: send IPEX offer with held credential
                    var offerResult = await _broker.EnqueueCommandAsync(SignifyOperation.IpexOfferAndSubmit,
                        svc => svc.IpexOfferAndSubmit(new IpexOfferSubmitArgs(
                            SenderNameOrPrefix: grantRequest.SenderNameOrPrefix,
                            RecipientPrefix: grantRequest.RecipientPrefix,
                            CredentialSaid: grantRequest.CredentialSaid,
                            ApplySaid: grantRequest.ApplySaid
                        )));
                    result = offerResult.IsSuccess
                        ? Result.Ok(offerResult.Value["offerSaid"]?.StringValue ?? "")
                        : Result.Fail<string>(offerResult.Errors);
                    break;
                }

                case (false, false):
                    // Full disclosure grant path: use existing PresentStep
                    result = await _primeDataService.PresentStep(
                        senderName, grantRequest.CredentialSaid, grantRequest.RecipientPrefix,
                        nameof(HandleAppRequestIpexOfferOrGrantPresentationRpcAsync));
                    break;
            }

            if (result.IsSuccess) {
                await PollNotificationsThrottledAsync();
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexGrantResponsePayload(true));
            }
            else {
                var errorMsg = result.Errors.Count > 0 ? result.Errors[0].Message : $"IPEX {actionLabel} presentation failed";
                logger.LogWarning(nameof(HandleAppRequestIpexOfferOrGrantPresentationRpcAsync) + ": {Error}", errorMsg);
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new IpexGrantResponsePayload(false, Error: errorMsg));
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppRequestIpexOfferOrGrantPresentationRpcAsync) + ": Error during IPEX grant presentation");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new IpexGrantResponsePayload(false, Error: ex.Message));
        }
    }

    private static RecursiveDictionary BuildVleiRules(string? privacyText = null) {
        var rules = new RecursiveDictionary();
        rules["d"] = new RecursiveValue { StringValue = "" };
        var usage = new RecursiveDictionary();
        usage["l"] = new RecursiveValue { StringValue = "Usage of a valid, unexpired, and non-revoked vLEI Credential, as defined in the associated Ecosystem Governance Framework, does not assert that the Legal Entity is trustworthy, honest, reputable in its business dealings, safe to do business with, or compliant with any laws or that an implied or expressly intended purpose will be fulfilled." };
        rules["usageDisclaimer"] = new RecursiveValue { Dictionary = usage };
        var issuance = new RecursiveDictionary();
        issuance["l"] = new RecursiveValue { StringValue = "All information in a valid, unexpired, and non-revoked vLEI Credential, as defined in the associated Ecosystem Governance Framework, is accurate as of the date the validation process was complete. The vLEI Credential has been issued to the legal entity or person named in the vLEI Credential as the subject; and the qualified vLEI Issuer exercised reasonable care to perform the validation process set forth in the vLEI Ecosystem Governance Framework." };
        rules["issuanceDisclaimer"] = new RecursiveValue { Dictionary = issuance };
        if (privacyText is not null) {
            var privacy = new RecursiveDictionary();
            privacy["l"] = new RecursiveValue { StringValue = privacyText };
            rules["privacyDisclaimer"] = new RecursiveValue { Dictionary = privacy };
        }
        return rules;
    }

    /// <summary>
    /// Execute an IPEX operation and send the result as an RPC response.
    /// Handles success (extracts SAID if present), failure, and exceptions uniformly.
    /// Used by IPEX apply, agree, and admit handlers.
    /// </summary>
    /// <summary>
    /// Ensures the schema identified by the given SAID is resolved in KERIA.
    /// Checks if already available, and if not, resolves via OOBI from manifest or default hosts.
    /// Returns true if the schema is available (already was, or successfully resolved).
    /// </summary>
    private async Task<bool> EnsureSchemaResolvedAsync(string schemaSaid, string callerName) {
        try {
            // Fast path: check session cache of previously resolved schemas
            var cachedResult = await _storageGateway.GetItem<ResolvedSchemas>(StorageArea.Session);
            if (cachedResult.IsSuccess && cachedResult.Value?.Schemas.ContainsKey(schemaSaid) == true) {
                logger.LogDebug("{Caller}: Schema {SchemaSaid} found in session cache", callerName, schemaSaid);
                return true;
            }

            // Check if schema is already in KERIA and cache the body
            var existingSchemaRaw = await _broker.EnqueueBackgroundAsync(SignifyOperation.GetSchemaRaw,
                svc => svc.GetSchemaRaw(schemaSaid));
            if (existingSchemaRaw.IsSuccess) {
                logger.LogDebug("{Caller}: Schema {SchemaSaid} already resolved in KERIA", callerName, schemaSaid);
                await AddToResolvedSchemaCacheAsync(schemaSaid, existingSchemaRaw.Value);
                return true;
            }

            logger.LogInformation("{Caller}: Schema {SchemaSaid} not found in KERIA, attempting to load via OOBI", callerName, schemaSaid);

            var schemaOobiUrls = _schemaService.GetOobiUrls(schemaSaid);
            if (schemaOobiUrls.Length == 0) {
                logger.LogInformation("{Caller}: Schema {SchemaSaid} not in manifest, trying default hosts", callerName, schemaSaid);
                schemaOobiUrls = [.. _schemaService.DefaultOobiHosts.Select(host => $"{host}/oobi/{schemaSaid}")];
            }
            else {
                var schemaEntry = _schemaService.GetSchema(schemaSaid);
                logger.LogInformation("{Caller}: Found schema '{SchemaName}' in manifest with {Count} OOBI URLs",
                    callerName, schemaEntry?.Name ?? schemaSaid, schemaOobiUrls.Length);
            }

            foreach (var schemaOobi in schemaOobiUrls) {
                try {
                    logger.LogInformation("{Caller}: Attempting to resolve schema OOBI: {Oobi}", callerName, schemaOobi);
                    var resolveResult = await _broker.EnqueueBackgroundAsync(SignifyOperation.ResolveOobi,
                        svc => svc.ResolveOobi(schemaOobi));
                    if (resolveResult.IsSuccess) {
                        var resolveOp = resolveResult.Value;
                        if (resolveOp.TryGetValue("name", out var nameValue) && nameValue.StringValue is string opName && !string.IsNullOrEmpty(opName)) {
                            logger.LogInformation("{Caller}: Waiting for schema OOBI resolution operation {OpName}", callerName, opName);
                            var op = new Operation(opName);
                            var waitResult = await _broker.EnqueueBackgroundAsync(SignifyOperation.WaitForOperation,
                                svc => svc.WaitForOperation(op));
                            if (waitResult.IsFailed) {
                                logger.LogWarning("{Caller}: Operation wait failed for schema OOBI: {Error}", callerName, waitResult.Errors[0].Message);
                                continue;
                            }
                        }

                        const int maxRetries = 5;
                        for (int attempt = 1; attempt <= maxRetries; attempt++) {
                            var verifyResult = await _broker.EnqueueBackgroundAsync(SignifyOperation.GetSchemaRaw,
                                svc => svc.GetSchemaRaw(schemaSaid));
                            if (verifyResult.IsSuccess) {
                                logger.LogInformation("{Caller}: Successfully loaded and verified schema {SchemaSaid} from {Oobi} (attempt {Attempt})",
                                    callerName, schemaSaid, schemaOobi, attempt);
                                await AddToResolvedSchemaCacheAsync(schemaSaid, verifyResult.Value);
                                return true;
                            }
                            if (attempt < maxRetries) {
                                logger.LogInformation("{Caller}: Schema {SchemaSaid} not yet available, retrying ({Attempt}/{MaxRetries})",
                                    callerName, schemaSaid, attempt, maxRetries);
                                await Task.Delay(1000);
                            }
                            else {
                                logger.LogWarning("{Caller}: Schema OOBI resolved but schema {SchemaSaid} still not available in KERIA after {MaxRetries} attempts",
                                    callerName, schemaSaid, maxRetries);
                            }
                        }
                    }
                    else {
                        logger.LogWarning("{Caller}: Failed to resolve schema OOBI {Oobi}: {Error}",
                            callerName, schemaOobi, resolveResult.Errors[0].Message);
                    }
                }
                catch (Exception ex) {
                    logger.LogWarning(ex, "{Caller}: Exception resolving schema OOBI {Oobi}", callerName, schemaOobi);
                }
            }

            logger.LogWarning("{Caller}: Could not load schema {SchemaSaid} from any known source", callerName, schemaSaid);
            return false;
        }
        catch (Exception ex) {
            logger.LogWarning(ex, "{Caller}: Exception in EnsureSchemaResolvedAsync for {SchemaSaid}", callerName, schemaSaid);
            return false;
        }
    }

    /// <summary>
    /// Resolves all schemas from the manifest in KERIA. Credentials are chained (e.g., ECR → ECR Auth → LE → QVI),
    /// and KERIA needs all schemas in the chain to verify and index a credential. Rather than trying to chase
    /// the chain from a single exchange, we proactively resolve all known schemas.
    /// Already-resolved schemas return immediately via the fast path in EnsureSchemaResolvedAsync.
    /// </summary>
    private async Task EnsureAllManifestSchemasResolvedAsync(string callerName) {
        var allSchemas = _schemaService.GetAllSchemas();
        foreach (var schema in allSchemas) {
            await EnsureSchemaResolvedAsync(schema.Said, callerName);
        }
    }

    private async Task AddToExchangeCacheAsync(string said, string rawJson) {
        try {
            var existing = await _storageGateway.GetItem<CachedExns>(StorageArea.Session);
            var exchanges = existing.IsSuccess && existing.Value is not null
                ? new Dictionary<string, string>(existing.Value.Exchanges)
                : new Dictionary<string, string>();
            exchanges[said] = rawJson;
            await _storageGateway.WriteTransaction(StorageArea.Session, tx => {
                tx.SetItem(new CachedExns { Exchanges = exchanges });
                tx.SetItem(NextSessionSequence());
            });
        }
        catch (Exception ex) {
            logger.LogDebug(ex, "AddToExchangeCacheAsync: Failed to update cache (non-critical)");
        }
    }

    private async Task<Result<RecursiveDictionary>> GetExchangeCachedAsync(string said) {
        try {
            var cached = await _storageGateway.GetItem<CachedExns>(StorageArea.Session);
            if (cached.IsSuccess && cached.Value?.Exchanges.TryGetValue(said, out var rawJson) == true) {
                var rd = JsonSerializer.Deserialize<RecursiveDictionary>(rawJson, JsonOptions.RecursiveDictionary);
                if (rd is not null) {
                    logger.LogDebug("GetExchangeCachedAsync: Cache hit for {Said}", said);
                    return Result.Ok(rd);
                }
            }
        }
        catch (Exception ex) {
            logger.LogDebug(ex, "GetExchangeCachedAsync: Cache read failed for {Said}, falling through to network", said);
        }

        var rawResult = await _broker.EnqueueReadAsync(SignifyOperation.GetExchangeRaw,
            svc => svc.GetExchangeRaw(said));
        if (rawResult.IsFailed) return Result.Fail<RecursiveDictionary>(rawResult.Errors);

        await AddToExchangeCacheAsync(said, rawResult.Value);

        var resultDict = JsonSerializer.Deserialize<RecursiveDictionary>(rawResult.Value, JsonOptions.RecursiveDictionary);
        if (resultDict is null) return Result.Fail<RecursiveDictionary>("Failed to deserialize exchange from raw JSON");
        return Result.Ok(resultDict);
    }

    private async Task AddToResolvedSchemaCacheAsync(string schemaSaid, string rawJson) {
        try {
            var existing = await _storageGateway.GetItem<ResolvedSchemas>(StorageArea.Session);
            var schemas = existing.IsSuccess && existing.Value is not null
                ? new Dictionary<string, string>(existing.Value.Schemas)
                : new Dictionary<string, string>();
            schemas[schemaSaid] = rawJson;
            await _storageGateway.WriteTransaction(StorageArea.Session, tx => {
                tx.SetItem(new ResolvedSchemas { Schemas = schemas });
                tx.SetItem(NextSessionSequence());
            });
        }
        catch (Exception ex) {
            logger.LogDebug(ex, "AddToResolvedSchemaCacheAsync: Failed to update cache (non-critical)");
        }
    }

    /// <summary>
    /// Fetches an exchange by SAID and extracts all schema SAIDs from its embedded ACDC:
    /// the credential's own schema (e.acdc.s) and any edge schemas (e.acdc.e.{name}.s).
    /// </summary>
    private async Task<List<string>> GetSchemaSaidsFromExchangeAsync(string exchangeSaid, string callerName) {
        var schemaSaids = new List<string>();
        try {
            var exnResult = await GetExchangeCachedAsync(exchangeSaid);
            if (exnResult.IsFailed) {
                logger.LogWarning("{Caller}: GetExchange failed for {Said}: {Error}",
                    callerName, exchangeSaid, exnResult.Errors.Count > 0 ? exnResult.Errors[0].Message : "unknown");
                return schemaSaids;
            }
            var view = ExchangeView.FromRecursiveDictionary(exnResult.Value);

            // Extract the credential's own schema SAID
            var mainSchema = view.E?.GetValueByPath("acdc.s")?.Value?.ToString();
            if (mainSchema is not null) {
                schemaSaids.Add(mainSchema);
            }

            // Extract edge schema SAIDs (e.g., acdc.e.auth.s for ECR Auth edge)
            var edges = view.E?.GetByPath("acdc.e")?.Dictionary;
            if (edges is not null) {
                foreach (var kvp in edges) {
                    if (kvp.Key == "d") continue; // Skip SAID placeholder
                    var edgeSchema = kvp.Value?.Dictionary?.GetByPath("s")?.StringValue;
                    if (edgeSchema is not null) {
                        schemaSaids.Add(edgeSchema);
                    }
                }
            }

            if (schemaSaids.Count > 0) {
                logger.LogInformation("{Caller}: Extracted {Count} schema SAIDs from exchange {ExchangeSaid}: [{Saids}]",
                    callerName, schemaSaids.Count, exchangeSaid, string.Join(", ", schemaSaids));
            }
            else {
                logger.LogDebug("{Caller}: No schema SAIDs found in exchange {ExchangeSaid}", callerName, exchangeSaid);
            }
            return schemaSaids;
        }
        catch (Exception ex) {
            logger.LogWarning(ex, "{Caller}: Exception extracting schema SAIDs from exchange {ExchangeSaid}", callerName, exchangeSaid);
            return schemaSaids;
        }
    }

    private async Task ExecuteIpexAndRespondAsync(
        string portId,
        string portSessionId,
        string rpcRequestId,
        string operationName,
        SignifyOperation brokerOp,
        Func<Task<Result<RecursiveDictionary>>> operation,
        string? saidKey = null,
        string? senderPrefix = null,
        Func<Task>? onSuccess = null) {
        try {
            logger.LogInformation("{Op}: Starting IPEX operation", operationName);
            var result = await _broker.EnqueueCommandAsync(brokerOp,
                _ => operation());

            if (result.IsSuccess) {
                var credentialPending = result.Value.TryGetValue("credentialPending", out var cpVal) ? cpVal?.BooleanValue : null;
                logger.LogInformation("{Op}: IPEX operation succeeded, credentialPending={CredentialPending}, keys=[{Keys}]",
                    operationName, credentialPending, string.Join(", ", result.Value.Keys));

                if (onSuccess is not null) {
                    await onSuccess();
                }

                if (saidKey is not null) {
                    var said = result.Value[saidKey]?.StringValue;
                    logger.LogInformation("{Op}: Responding with {SaidKey}={Said}", operationName, saidKey, said);
                    await _portService.SendRpcResponseAsync(portId, portSessionId, rpcRequestId,
                        result: new IpexResponsePayload(Said: said, SenderPrefix: senderPrefix));
                }
                else {
                    await _portService.SendRpcResponseAsync(portId, portSessionId, rpcRequestId,
                        result: new IpexAdmitResponsePayload(true));
                }
            }
            else {
                var errorMsg = result.Errors.Count > 0 ? result.Errors[0].Message : $"IPEX {operationName} failed";
                logger.LogWarning("{Op}: IPEX operation failed: {Error}", operationName, errorMsg);
                await _portService.SendRpcResponseAsync(portId, portSessionId, rpcRequestId,
                    errorMessage: errorMsg);
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, "{Op}: Error during IPEX operation", operationName);
            await _portService.SendRpcResponseAsync(portId, portSessionId, rpcRequestId,
                errorMessage: ex.Message);
        }
    }

    /// <summary>
    /// Handles /KeriAuth/connection/invite from ContentScript.
    /// Resolves the page's OOBI, then creates a pending request for App to show approval UI.
    /// The RPC response to CS is deferred until HandleAppReplyConnectionInviteRpcAsync.
    /// </summary>
    private async Task HandleConnectionInviteRpcAsync(string portId, PortSession portSession, RpcRequest request, int tabId, string? tabUrl, string origin) {
        logger.LogInformation(nameof(HandleConnectionInviteRpcAsync) + ": tabId={TabId}, origin={Origin}", tabId, origin);

        // Extract params
        var rpcParams = request.GetParams<ConnectionInviteRpcParams>();
        var oobi = rpcParams?.Payload?.Oobi;
        var originalRequestId = rpcParams?.RequestId ?? request.Id;

        if (string.IsNullOrEmpty(oobi)) {
            logger.LogWarning(nameof(HandleConnectionInviteRpcAsync) + ": Missing or empty OOBI");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: "Missing or empty OOBI in connection invite");
            return;
        }

        // Resolve the page's OOBI to validate it and extract identity info
        string? resolvedAidPrefix = null;
        string? resolvedAlias = null;
        var resolveResult = await _broker.EnqueueCommandAsync(SignifyOperation.ResolveOobi,
            svc => svc.ResolveOobi(oobi));
        if (resolveResult.IsSuccess && resolveResult.Value is not null) {
            // Best-effort extraction of AID prefix from resolved result
            resolvedAidPrefix = resolveResult.Value.GetByPath("response.i")?.StringValue;
            resolvedAlias = resolveResult.Value.GetByPath("response.alias")?.StringValue;
            logger.LogInformation(nameof(HandleConnectionInviteRpcAsync) + ": Resolved OOBI, prefix={Prefix}, alias={Alias}",
                resolvedAidPrefix ?? "null", resolvedAlias ?? "null");
        }
        else {
            // Log warning but continue — user can still approve with limited info
            logger.LogWarning(nameof(HandleConnectionInviteRpcAsync) + ": Failed to resolve page OOBI: {Error}",
                resolveResult.Errors.Count > 0 ? resolveResult.Errors[0].Message : "Unknown error");
        }

        // Build payload for App UI
        var invitePayload = new ConnectionInviteRequestPayload(
            Oobi: oobi,
            ResolvedAidPrefix: resolvedAidPrefix,
            ResolvedAlias: resolvedAlias,
            TabUrl: tabUrl
        );

        // Create pending request — store port routing info for deferred CS response
        var pendingRequest = new PendingBwAppRequest {
            RequestId = originalRequestId,
            Type = BwAppMessageType.Values.RequestConnectionInvite,
            Payload = invitePayload,
            CreatedAtUtc = DateTime.UtcNow,
            TabId = tabId,
            TabUrl = tabUrl,
            PortId = portId,
            PortSessionId = portSession.PortSessionId.ToString(),
            RpcRequestId = request.Id
        };

        await UseSidePanelOrActionPopupAsync(pendingRequest);
        // RPC response to CS will be sent when App replies via HandleAppReplyConnectionInviteRpcAsync
    }

    /// <summary>
    /// Handles /KeriAuth/ipex/apply from ContentScript.
    /// Web page requests the user to send an IPEX apply message (request for credential).
    /// Creates a pending request for App to show approval UI.
    /// </summary>
    private async Task HandleIpexApplyRpcAsync(string portId, PortSession portSession, RpcRequest request,
        int tabId, string? tabUrl, string origin) {
        logger.LogInformation(nameof(HandleIpexApplyRpcAsync) + ": tabId={TabId}, origin={Origin}", tabId, origin);

        var rpcParams = request.GetParams<IpexApplyRpcParams>();
        var applyPayload = rpcParams?.Payload;
        var originalRequestId = rpcParams?.RequestId ?? request.Id;

        if (applyPayload is null || string.IsNullOrEmpty(applyPayload.SchemaSaid) || string.IsNullOrEmpty(applyPayload.RecipientPrefix)) {
            logger.LogWarning(nameof(HandleIpexApplyRpcAsync) + ": invalid payload - schemaSaid and recipient required");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: "Invalid payload for ipex/apply: schemaSaid and recipient required");
            return;
        }

        // Validate isPresentation consistency with IPEX protocol
        // Apply in Issuance: requester applies to issuer (sender is NOT the issuer) — valid
        // Apply in Presentation: requester applies to holder (sender requests credential presentation) — valid
        // Both are valid combinations for Apply, so just log for observability
        logger.LogInformation(nameof(HandleIpexApplyRpcAsync) + ": isPresentation={IsPresentation}, schemaSaid={SchemaSaid}, recipient={Recipient}",
            applyPayload.IsPresentation, applyPayload.SchemaSaid, applyPayload.RecipientPrefix);

        var requestIpexApplyPayload = new RequestIpexApplyPayload(
            Origin: origin,
            SchemaSaid: applyPayload.SchemaSaid,
            RecipientPrefix: applyPayload.RecipientPrefix,
            IsPresentation: applyPayload.IsPresentation,
            Attributes: applyPayload.Attributes,
            TabId: tabId,
            TabUrl: tabUrl,
            OriginalRequestId: originalRequestId,
            OriginalType: CsBwMessageTypes.IPEX_APPLY
        );

        var pendingRequest = new PendingBwAppRequest {
            RequestId = originalRequestId,
            Type = BwAppMessageType.Values.RequestIpexApply,
            Payload = requestIpexApplyPayload,
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
    /// Handles /KeriAuth/ipex/agree from ContentScript.
    /// Web page requests the user to send an IPEX agree message (agree to an offer).
    /// Creates a pending request for App to show approval UI.
    /// </summary>
    private async Task HandleIpexAgreeRpcAsync(string portId, PortSession portSession, RpcRequest request,
        int tabId, string? tabUrl, string origin) {
        logger.LogInformation(nameof(HandleIpexAgreeRpcAsync) + ": tabId={TabId}, origin={Origin}", tabId, origin);

        var rpcParams = request.GetParams<IpexAgreeRpcParams>();
        var agreePayload = rpcParams?.Payload;
        var originalRequestId = rpcParams?.RequestId ?? request.Id;

        if (agreePayload is null || string.IsNullOrEmpty(agreePayload.OfferSaid) || string.IsNullOrEmpty(agreePayload.RecipientPrefix)) {
            logger.LogWarning(nameof(HandleIpexAgreeRpcAsync) + ": invalid payload - offerSaid and recipient required");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: "Invalid payload for ipex/agree: offerSaid and recipient required");
            return;
        }

        // Agree applies in both issuance (agree to an offer) and presentation flows
        logger.LogInformation(nameof(HandleIpexAgreeRpcAsync) + ": isPresentation={IsPresentation}, offerSaid={OfferSaid}, recipient={Recipient}",
            agreePayload.IsPresentation, agreePayload.OfferSaid, agreePayload.RecipientPrefix);

        var requestIpexAgreePayload = new RequestIpexAgreePayload(
            Origin: origin,
            OfferSaid: agreePayload.OfferSaid,
            RecipientPrefix: agreePayload.RecipientPrefix,
            IsPresentation: agreePayload.IsPresentation,
            TabId: tabId,
            TabUrl: tabUrl,
            OriginalRequestId: originalRequestId,
            OriginalType: CsBwMessageTypes.IPEX_AGREE
        );

        var pendingRequest = new PendingBwAppRequest {
            RequestId = originalRequestId,
            Type = BwAppMessageType.Values.RequestIpexAgree,
            Payload = requestIpexAgreePayload,
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
    /// Handles /KeriAuth/ipex/admit from ContentScript (webpage-initiated).
    /// Web page requests the user to send an IPEX admit message (acknowledge receipt of a grant).
    /// Creates a pending request for App to show approval UI.
    /// </summary>
    private async Task HandleIpexAdmitFromPageRpcAsync(string portId, PortSession portSession, RpcRequest request,
        int tabId, string? tabUrl, string origin) {
        logger.LogInformation(nameof(HandleIpexAdmitFromPageRpcAsync) + ": tabId={TabId}, origin={Origin}", tabId, origin);

        var rpcParams = request.GetParams<IpexAdmitRpcParams>();
        var admitPayload = rpcParams?.Payload;
        var originalRequestId = rpcParams?.RequestId ?? request.Id;

        if (admitPayload is null || string.IsNullOrEmpty(admitPayload.GrantSaid) || string.IsNullOrEmpty(admitPayload.RecipientPrefix)) {
            logger.LogWarning(nameof(HandleIpexAdmitFromPageRpcAsync) + ": invalid payload - grantSaid and recipient required");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: "Invalid payload for ipex/admit: grantSaid and recipient required");
            return;
        }

        logger.LogInformation(nameof(HandleIpexAdmitFromPageRpcAsync) + ": isPresentation={IsPresentation}, grantSaid={GrantSaid}, recipient={Recipient}",
            admitPayload.IsPresentation, admitPayload.GrantSaid, admitPayload.RecipientPrefix);

        var requestIpexAdmitPayload = new RequestIpexAdmitPayload(
            Origin: origin,
            GrantSaid: admitPayload.GrantSaid,
            RecipientPrefix: admitPayload.RecipientPrefix,
            IsPresentation: admitPayload.IsPresentation,
            TabId: tabId,
            TabUrl: tabUrl,
            OriginalRequestId: originalRequestId,
            OriginalType: CsBwMessageTypes.IPEX_ADMIT
        );

        var pendingRequest = new PendingBwAppRequest {
            RequestId = originalRequestId,
            Type = BwAppMessageType.Values.RequestIpexAdmitFromPage,
            Payload = requestIpexAdmitPayload,
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
    /// Handles /KeriAuth/connection/confirm from ContentScript.
    /// Page confirms it resolved the reciprocal OOBI. Persists the connection and notifies App.
    /// </summary>
    private async Task HandleConnectionConfirmRpcAsync(string portId, PortSession portSession, RpcRequest request, int tabId, string? tabUrl) {
        logger.LogInformation(nameof(HandleConnectionConfirmRpcAsync) + ": tabId={TabId}", tabId);

        // Extract params
        var rpcParams = request.GetParams<ConnectionConfirmRpcParams>();
        var oobi = rpcParams?.Payload?.Oobi;
        var error = rpcParams?.Payload?.Error;

        if (string.IsNullOrEmpty(oobi)) {
            logger.LogWarning(nameof(HandleConnectionConfirmRpcAsync) + ": Missing OOBI in confirm");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new { ok = true });
            return;
        }

        if (!string.IsNullOrEmpty(error)) {
            logger.LogWarning(nameof(HandleConnectionConfirmRpcAsync) + ": Page reported error: {Error}", error);
            _pendingConnections.Remove(oobi);
            // Notify App of the failure
            await BroadcastConnectionConfirmedAsync(oobi, error);
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new { ok = true });
            return;
        }

        // Clean up pending connection info (connection was already persisted in reply handler)
        _pendingConnections.Remove(oobi);

        logger.LogInformation(nameof(HandleConnectionConfirmRpcAsync) + ": Connection confirmed by page for OOBI");

        // Notify App to update connections UI
        await BroadcastConnectionConfirmedAsync(oobi, null);

        await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
            result: new { ok = true });
    }

    /// <summary>
    /// Broadcasts a connection confirmed notification to App via chrome.runtime.sendMessage.
    /// </summary>
    private async Task BroadcastConnectionConfirmedAsync(string oobi, string? error) {
        try {
            await _jsRuntime.InvokeVoidAsync("chrome.runtime.sendMessage",
                new {
                    t = SendMessageTypes.SwAppWake,
                    notification = BwAppMessageType.Values.NotifyConnectionConfirmed,
                    oobi,
                    error
                });
        }
        catch (Exception ex) {
            logger.LogDebug(ex, nameof(BroadcastConnectionConfirmedAsync) + ": Failed to broadcast (App may not be open)");
        }
    }

    /// <summary>
    /// Extracts the AID prefix from an OOBI URL.
    /// Expected format: /oobi/{PREFIX}/agent/{AGENT} or /oobi/{PREFIX}
    /// </summary>
    private static string? ExtractPrefixFromOobi(string oobi) {
        try {
            var uri = new Uri(oobi);
            var segments = uri.AbsolutePath.Split('/');
            var oobiIndex = Array.IndexOf(segments, "oobi");
            if (oobiIndex >= 0 && oobiIndex + 1 < segments.Length) {
                return segments[oobiIndex + 1];
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Handles AppBw.ReplyConnectionInvite from App.
    /// User approved the connection — generates own OOBI and sends it back to CS.
    /// </summary>
    private async Task HandleAppReplyConnectionInviteRpcAsync(string portId, RpcRequest request, int tabId, string? requestId, JsonElement? payload) {
        logger.LogInformation(nameof(HandleAppReplyConnectionInviteRpcAsync) + ": tabId={TabId}, requestId={RequestId}", tabId, requestId);

        if (requestId is null) {
            logger.LogWarning(nameof(HandleAppReplyConnectionInviteRpcAsync) + ": Missing requestId");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: "Missing requestId");
            return;
        }

        // Get pending request to retrieve port routing info and original payload
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
            // Deserialize the reply payload
            if (payload is null || !payload.HasValue) {
                logger.LogWarning(nameof(HandleAppReplyConnectionInviteRpcAsync) + ": Missing payload");
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: "Missing payload");
                return;
            }

            var replyPayload = JsonSerializer.Deserialize<ConnectionInviteReplyPayload>(payload.Value.GetRawText(), JsonOptions.CamelCase);
            if (replyPayload is null) {
                logger.LogWarning(nameof(HandleAppReplyConnectionInviteRpcAsync) + ": Could not deserialize ConnectionInviteReplyPayload");
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: "Invalid reply payload");
                return;
            }

            logger.LogInformation(nameof(HandleAppReplyConnectionInviteRpcAsync) + ": aidName={AidName}", replyPayload.AidName);

            // Get own OOBI for the selected AID
            var oobiResult = await _broker.EnqueueReadAsync(SignifyOperation.GetOobi,
                svc => svc.GetOobi(replyPayload.AidName, "agent"));
            if (oobiResult.IsFailed || oobiResult.Value is null) {
                var errorMsg = oobiResult.Errors.Count > 0 ? oobiResult.Errors[0].Message : "Failed to get OOBI";
                logger.LogError(nameof(HandleAppReplyConnectionInviteRpcAsync) + ": GetOobi failed: {Error}", errorMsg);

                // Send error to CS if we have port info
                if (pendingRequest?.PortId is not null && pendingRequest.PortSessionId is not null) {
                    await _portService.SendRpcResponseAsync(
                        pendingRequest.PortId,
                        pendingRequest.PortSessionId,
                        pendingRequest.RpcRequestId ?? requestId,
                        errorMessage: errorMsg);
                }

                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: errorMsg);
                return;
            }

            // Extract OOBI URL from the result (same pattern as HandleAppRequestGetOobiRpcAsync)
            string? myOobiUrl = null;
            if (oobiResult.Value.TryGetValue("oobis", out var oobisVal)) {
                myOobiUrl = oobisVal?.List?.FirstOrDefault()?.StringValue
                            ?? oobisVal?.StringValue;
            }

            if (string.IsNullOrEmpty(myOobiUrl)) {
                logger.LogError(nameof(HandleAppReplyConnectionInviteRpcAsync) + ": GetOobi returned no OOBI URL");
                if (pendingRequest?.PortId is not null && pendingRequest.PortSessionId is not null) {
                    await _portService.SendRpcResponseAsync(
                        pendingRequest.PortId,
                        pendingRequest.PortSessionId,
                        pendingRequest.RpcRequestId ?? requestId,
                        errorMessage: "Failed to generate OOBI URL");
                }
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    errorMessage: "Failed to generate OOBI URL");
                return;
            }

            // Extract own AID prefix from our OOBI URL for connection storage later
            var myAidPrefix = ExtractPrefixFromOobi(myOobiUrl) ?? "";

            // Retrieve the page's original OOBI for correlation
            string? pageOobi = null;
            string? resolvedAlias = null;
            if (pendingRequest?.Payload is not null) {
                try {
                    var payloadJson = JsonSerializer.Serialize(pendingRequest.Payload, JsonOptions.CamelCase);
                    var originalPayload = JsonSerializer.Deserialize<ConnectionInviteRequestPayload>(payloadJson, JsonOptions.CamelCase);
                    pageOobi = originalPayload?.Oobi;
                    resolvedAlias = originalPayload?.ResolvedAlias;
                }
                catch (Exception ex) {
                    logger.LogWarning(ex, nameof(HandleAppReplyConnectionInviteRpcAsync) + ": Could not extract original payload");
                }
            }

            // Store pending connection info for the confirm handler
            if (!string.IsNullOrEmpty(pageOobi)) {
                _pendingConnections[pageOobi] = new PendingConnectionInfo(replyPayload.AidName, myAidPrefix, resolvedAlias);
            }

            // Persist connection immediately on user approval (don't wait for confirm)
            try {
                string? remotePrefix = !string.IsNullOrEmpty(pageOobi) ? ExtractPrefixFromOobi(pageOobi) : null;
                var connectionName = replyPayload.ConnectionName
                    ?? resolvedAlias
                    ?? replyPayload.AidName;

                var connection = new Connection {
                    Name = connectionName,
                    SenderPrefix = myAidPrefix,
                    ReceiverPrefix = remotePrefix ?? "",
                    ConnectionDate = DateTime.UtcNow
                };

                var prefsResult2 = await _storageGateway.GetItem<Preferences>();
                var digest2 = prefsResult2.IsSuccess ? prefsResult2.Value?.SelectedKeriaConnectionDigest : null;
                if (!string.IsNullOrEmpty(digest2)) {
                    var configsResult2 = await _storageGateway.GetItem<KeriaConnectConfigs>();
                    if (configsResult2.IsSuccess && configsResult2.Value?.Configs.TryGetValue(digest2, out KeriaConnectConfig? cfg) == true) {
                        var updatedCfg = cfg with { Connections = [.. cfg.Connections, connection] };
                        var updatedDict = new Dictionary<string, KeriaConnectConfig>(configsResult2.Value.Configs) { [digest2] = updatedCfg };
                        await _storageGateway.SetItem(configsResult2.Value with { Configs = updatedDict });
                    }
                }

                logger.LogInformation(nameof(HandleAppReplyConnectionInviteRpcAsync) + ": Connection persisted, name={Name}, sender={Sender}, receiver={Receiver}",
                    connection.Name, connection.SenderPrefix, connection.ReceiverPrefix);
            }
            catch (Exception storageEx) {
                logger.LogError(storageEx, nameof(HandleAppReplyConnectionInviteRpcAsync) + ": Failed to persist connection");
            }

            // Connection shared — start burst polling for expected reciprocal notifications
            RestartNotificationBurst();

            // Append friendly name to outbound OOBI as ?name= parameter
            if (!string.IsNullOrWhiteSpace(replyPayload.FriendlyName)) {
                var encodedName = Uri.EscapeDataString(replyPayload.FriendlyName);
                var separator = myOobiUrl.Contains('?') ? "&" : "?";
                myOobiUrl = $"{myOobiUrl}{separator}name={encodedName}";
            }

            // Route the ConnectionInviteResponse to CS
            var csResponse = new ConnectionInviteResponse(Oobi: myOobiUrl);
            if (pendingRequest?.PortId is not null && pendingRequest.PortSessionId is not null) {
                logger.LogInformation(nameof(HandleAppReplyConnectionInviteRpcAsync) + ": Sending reciprocal OOBI to CS, requestId={RequestId}", requestId);
                await _portService.SendRpcResponseAsync(
                    pendingRequest.PortId,
                    pendingRequest.PortSessionId,
                    pendingRequest.RpcRequestId ?? requestId,
                    result: csResponse);
            }
            else {
                logger.LogWarning(nameof(HandleAppReplyConnectionInviteRpcAsync) + ": No port info for CS response, requestId={RequestId}. Response not sent.", requestId);
            }

            // Acknowledge the App RPC
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id, result: new { success = true });
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleAppReplyConnectionInviteRpcAsync) + ": Error processing connection invite reply");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts the "said" string from an EventMessage.Data payload.
    /// Data is typically a JsonElement after wire deserialization.
    /// Returns null if the payload is missing or malformed.
    /// </summary>
    private static string? ExtractSaidFromEventData(object? data) {
        if (data is null) return null;
        if (data is JsonElement element && element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("said", out var saidProp)
            && saidProp.ValueKind == JsonValueKind.String) {
            return saidProp.GetString();
        }
        return null;
    }

    /// <summary>
    /// Handles EVENT messages from ContentScript or App (fire-and-forget notifications).
    /// Events don't require a response - they're used for notifications like user activity.
    /// </summary>
    private async Task HandlePortEventAsync(string portId, PortSession? portSession, Models.Messages.Port.EventMessage eventMessage) {
        logger.LogDebug(nameof(HandlePortEventAsync) + ": name={Name}, portId={PortId}", eventMessage.Name, portId);

        switch (eventMessage.Name) {
            case AppBwMessageType.Values.UserActivity:
                // Extend session on user activity
                logger.LogDebug(nameof(HandlePortEventAsync) + ": User activity event received, extending session");
                if (await _sessionManager.ExtendIfUnlockedAsync()) {
                    await _storageGateway.SetItem(NextSessionSequence(), StorageArea.Session);
                }
                // Only start burst if not already active — avoid polling when signify client isn't connected (e.g., after SW restart)
                if (_notificationPollingCts is null || _notificationPollingCts.IsCancellationRequested) {
                    RestartNotificationBurst();
                }
                break;

            case AppBwMessageType.Values.RequestAcknowledgeMigrationNotice:
                // Fire-and-forget from App after user dismisses the migration warning banner.
                // BW owns all writes to MigrationNotice; App is a passive displayer.
                logger.LogInformation(nameof(HandlePortEventAsync) + ": RequestAcknowledgeMigrationNotice event received");
                try {
                    var removeResult = await _storageGateway.RemoveItem<MigrationNotice>();
                    if (removeResult.IsFailed) {
                        logger.LogWarning(nameof(HandlePortEventAsync) + ": Failed to remove MigrationNotice: {Error}",
                            string.Join("; ", removeResult.Errors.Select(e => e.Message)));
                    }
                }
                catch (Exception ex) {
                    logger.LogError(ex, nameof(HandlePortEventAsync) + ": Error handling RequestAcknowledgeMigrationNotice");
                }
                break;

            case AppBwMessageType.Values.RequestLockSession:
                // Lock request from App. Clear passcode and session storage FIRST to prevent
                // any concurrent fire-and-forget observer (e.g., PreferencesObserver calling
                // ExtendIfUnlockedAsync) from re-writing SessionStateModel during the lock.
                logger.LogInformation(nameof(HandlePortEventAsync) + ": RequestLockSession event received");
                try {
                    // 1. Clear session state first — this nulls _passcode, which guards
                    //    ExtendIfUnlockedAsync from writing SessionStateModel
                    await _sessionManager.LockSessionAsync();
                    // 2. Write sequence marker so App's WaitForAppCache can detect the lock
                    await _storageGateway.SetItem(NextSessionSequence(), StorageArea.Session);
                    // 3. Slower cleanup after session is authoritatively locked
                    await _broker.EnqueueCommandAsync(SignifyOperation.Disconnect, async svc => { await svc.Disconnect(); return Result.Ok(); });
                    await CancelNotificationPollingAsync();
                }
                catch (Exception ex) {
                    logger.LogError(ex, nameof(HandlePortEventAsync) + ": Error handling RequestLockSession");
                }
                break;

            case AppBwMessageType.Values.AppClosed:
                // App closed notification - handle cleanup if needed
                logger.LogDebug(nameof(HandlePortEventAsync) + ": App closed event received from portId={PortId}", portId);
                // Port disconnect will handle cleanup
                break;

            case AppBwMessageType.Values.RequestPollNotifications:
                // Fire-and-forget burst poll trigger from App (e.g., NotificationsPage mount).
                // App observes updated notifications via CachedNotifications storage observer.
                logger.LogDebug(nameof(HandlePortEventAsync) + ": RequestPollNotifications event received");
                if (_notificationPollingCts is null || _notificationPollingCts.IsCancellationRequested) {
                    RestartNotificationBurst();
                }
                break;

            case AppBwMessageType.Values.RequestBurstPoll:
                // User-initiated refresh (e.g., clicking DIGN logo in AppBar).
                // Refreshes all three resources. Each method has its own throttle skip,
                // so rapid repeated clicks coalesce naturally via PollingState timestamps.
                logger.LogInformation(nameof(HandlePortEventAsync) + ": RequestBurstPoll event received");
                try {
                    RestartNotificationBurst();
                    await RefreshIdentifiersCache();
                    await RefreshCachedCredentialsAsync();
                }
                catch (Exception ex) {
                    logger.LogError(ex, nameof(HandlePortEventAsync) + ": Error handling RequestBurstPoll");
                }
                break;

            case AppBwMessageType.Values.RequestMarkNotification: {
                // Fire-and-forget from App. BW marks the notification in KERIA and refreshes
                // the CachedNotifications session record; the App observes the update via storage.
                logger.LogInformation(nameof(HandlePortEventAsync) + ": RequestMarkNotification event received");
                try {
                    var saidValue = ExtractSaidFromEventData(eventMessage.Data);
                    if (string.IsNullOrEmpty(saidValue)) {
                        logger.LogWarning(nameof(HandlePortEventAsync) + ": RequestMarkNotification missing said");
                        break;
                    }
                    if (!_signifyClientService.IsConnected) {
                        logger.LogWarning(nameof(HandlePortEventAsync) + ": RequestMarkNotification ignored — signify not connected");
                        break;
                    }
                    var markResult = await _broker.EnqueueCommandAsync(SignifyOperation.MarkNotification,
                        svc => svc.MarkNotification(saidValue));
                    if (markResult.IsSuccess) {
                        await PollNotificationsThrottledAsync();
                    }
                    else {
                        logger.LogWarning(nameof(HandlePortEventAsync) + ": MarkNotification failed: {Error}",
                            markResult.Errors.Count > 0 ? markResult.Errors[0].Message : "unknown");
                    }
                }
                catch (Exception ex) {
                    logger.LogError(ex, nameof(HandlePortEventAsync) + ": Error handling RequestMarkNotification");
                }
                break;
            }

            case AppBwMessageType.Values.RequestDeleteNotification: {
                // Fire-and-forget from App. BW deletes the notification in KERIA and refreshes
                // the CachedNotifications session record; the App observes the update via storage.
                logger.LogInformation(nameof(HandlePortEventAsync) + ": RequestDeleteNotification event received");
                try {
                    var saidValue = ExtractSaidFromEventData(eventMessage.Data);
                    if (string.IsNullOrEmpty(saidValue)) {
                        logger.LogWarning(nameof(HandlePortEventAsync) + ": RequestDeleteNotification missing said");
                        break;
                    }
                    if (!_signifyClientService.IsConnected) {
                        logger.LogWarning(nameof(HandlePortEventAsync) + ": RequestDeleteNotification ignored — signify not connected");
                        break;
                    }
                    var deleteResult = await _broker.EnqueueCommandAsync(SignifyOperation.DeleteNotification,
                        svc => svc.DeleteNotification(saidValue));
                    if (deleteResult.IsSuccess) {
                        await PollNotificationsThrottledAsync();
                    }
                    else {
                        logger.LogWarning(nameof(HandlePortEventAsync) + ": DeleteNotification failed: {Error}",
                            deleteResult.Errors.Count > 0 ? deleteResult.Errors[0].Message : "unknown");
                    }
                }
                catch (Exception ex) {
                    logger.LogError(ex, nameof(HandlePortEventAsync) + ": Error handling RequestDeleteNotification");
                }
                break;
            }

            default:
                logger.LogDebug(nameof(HandlePortEventAsync) + ": Unhandled event: name={Name}", eventMessage.Name);
                break;
        }
    }

    /// <summary>
    /// Handles authorize RPC request from ContentScript.
    /// Creates a pending request and opens the popup/sidepanel for user interaction.
    /// </summary>
    private async Task HandleSelectAuthorizeRpcAsync(string portId, PortSession portSession, RpcRequest request,
        int tabId, string? tabUrl, string origin) {
        logger.LogInformation(nameof(HandleSelectAuthorizeRpcAsync) + ": method={Method}, tabId={TabId}, origin={Origin}",
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
        logger.LogInformation(nameof(HandleSignRequestRpcAsync) + ": tabId={TabId}, origin={Origin}", tabId, origin);

        if (tabUrl is null) {
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                errorMessage: "Tab URL is required for sign-request");
            return;
        }

        // Extract typed params
        var rpcParams = request.GetParams<SignRequestRpcParams>();
        var signRequestPayload = rpcParams?.Payload;

        if (signRequestPayload is null) {
            logger.LogWarning(nameof(HandleSignRequestRpcAsync) + ": invalid payload");
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
            // Read SelectedPrefix from current KeriaConnectConfig
            var prefsResult = await _storageGateway.GetItem<Preferences>();
            var selectedDigest = prefsResult.IsSuccess && prefsResult.Value is not null
                ? prefsResult.Value.SelectedKeriaConnectionDigest
                : string.Empty;

            if (!string.IsNullOrEmpty(selectedDigest)) {
                var configsResult = await _storageGateway.GetItem<KeriaConnectConfigs>();
                if (configsResult.IsSuccess && configsResult.Value?.Configs.TryGetValue(selectedDigest, out var config) == true) {
                    rememberedPrefix = config.SelectedPrefix;
                }
            }

            // Fallback to empty if no config found
            rememberedPrefix ??= string.Empty;
        }

        // TODO P3: don't auto-approve sign headers when cross-origin request

        // Create pending request for sign headers
        var signPayload = new RequestSignHeadersPayload(
            Origin: origin,
            Url: requestUrl,
            Method: method,
            Headers: headers ?? [],
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
        logger.LogInformation(nameof(HandleSignDataRpcAsync) + ": tabId={TabId}, origin={Origin}", tabId, origin);

        // Extract typed params
        var rpcParams = request.GetParams<SignDataRpcParams>();
        var signDataPayload = rpcParams?.Payload;
        var originalRequestId = rpcParams?.RequestId ?? request.Id;

        if (signDataPayload is null || signDataPayload.Items is null || signDataPayload.Items.Length == 0) {
            logger.LogWarning(nameof(HandleSignDataRpcAsync) + ": invalid payload - no items");
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
        logger.LogInformation(nameof(HandleCreateDataAttestationRpcAsync) + ": tabId={TabId}, origin={Origin}", tabId, origin);

        // Extract typed params
        var rpcParams = request.GetParams<CreateDataAttestationRpcParams>();
        var createPayload = rpcParams?.Payload;
        var originalRequestId = rpcParams?.RequestId ?? request.Id;

        if (createPayload is null) {
            logger.LogWarning(nameof(HandleCreateDataAttestationRpcAsync) + ": invalid payload");
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
            logger.LogWarning(nameof(TransformToPolariWebAuthorizeResult) + ": payload is null");
            return null;
        }

        try {
            // Deserialize the payload to AppBw AuthorizeResult (internal format with Aid)
            var payloadJson = JsonSerializer.Serialize(payload, JsonOptions.RecursiveDictionary);
            var authorizeResult = JsonSerializer.Deserialize<AppBwAuthorizeResult>(payloadJson, JsonOptions.RecursiveDictionary);

            if (authorizeResult is null) {
                logger.LogWarning(nameof(TransformToPolariWebAuthorizeResult) + ": Could not deserialize to AuthorizeResult");
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

            logger.LogInformation(nameof(TransformToPolariWebAuthorizeResult) + ": Transformed payload with identifier prefix={Prefix}",
                identifier?.Prefix ?? "null");

            return result;
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(TransformToPolariWebAuthorizeResult) + ": Error transforming payload");
            return null;
        }
    }

    /// <summary>
    /// Handles RequestAddIdentifier RPC from App to create a new identifier.
    /// Creates AID with end role via signify-ts, refreshes identifiers cache, and updates storage.
    /// </summary>
    private async Task HandleRequestAddIdentifierRpcAsync(string portId, RpcRequest request, JsonElement? payload) {
        logger.LogInformation(nameof(HandleRequestAddIdentifierRpcAsync) + ": called");

        if (!await RequireSignifyConnectionAsync(portId, request.PortSessionId, request.Id)) {
            return;
        }

        try {
            if (!payload.HasValue) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new AddIdentifierResponse(Success: false, Error: "Missing payload"));
                return;
            }

            var addRequest = JsonSerializer.Deserialize<RequestAddIdentifierPayload>(
                payload.Value.GetRawText(), JsonOptions.CamelCase);

            if (addRequest is null || string.IsNullOrEmpty(addRequest.Alias)) {
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new AddIdentifierResponse(Success: false, Error: "Invalid payload or empty alias"));
                return;
            }

            // Create the identifier with end role
            var createResult = await _broker.EnqueueCommandAsync(SignifyOperation.CreateAidWithEndRole,
                svc => svc.CreateAidWithEndRole(addRequest.Alias));
            if (createResult.IsFailed || createResult.Value is null) {
                var errorMsg = string.Join("; ", createResult.Errors.Select(e => e.Message));
                logger.LogWarning(nameof(HandleRequestAddIdentifierRpcAsync) + ": Failed to create identifier: {Errors}", errorMsg);
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new AddIdentifierResponse(Success: false, Error: $"Failed to create identifier: {errorMsg}"));
                return;
            }

            logger.LogInformation(nameof(HandleRequestAddIdentifierRpcAsync) + ": Created identifier '{Alias}' with prefix {Prefix}",
                addRequest.Alias, createResult.Value.Prefix);

            // Refresh identifiers from KERIA
            var identifiersResult = await _broker.EnqueueReadAsync(SignifyOperation.GetIdentifiers,
                svc => svc.GetIdentifiers());
            if (identifiersResult.IsFailed) {
                var errorMsg = string.Join("; ", identifiersResult.Errors.Select(e => e.Message));
                logger.LogWarning(nameof(HandleRequestAddIdentifierRpcAsync) + ": Failed to refresh identifiers: {Errors}", errorMsg);
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new AddIdentifierResponse(Success: false, Error: $"Identifier created but failed to refresh list: {errorMsg}"));
                return;
            }

            // Update storage with new identifiers list
            var connectionInfoResult = await _storageGateway.GetItem<KeriaConnectionInfo>(StorageArea.Session);
            if (connectionInfoResult.IsFailed || connectionInfoResult.Value is null) {
                logger.LogWarning(nameof(HandleRequestAddIdentifierRpcAsync) + ": Failed to get KeriaConnectionInfo from storage");
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new AddIdentifierResponse(Success: false, Error: "Failed to get connection info from storage"));
                return;
            }

            var ps = await GetCurrentPollingStateAsync();
            var setResult = await _storageGateway.WriteTransaction(StorageArea.Session, tx => {
                tx.SetItem(new CachedIdentifiers { IdentifiersList = [identifiersResult.Value] });
                tx.SetItem(ps with { IdentifiersLastFetchedUtc = DateTime.UtcNow });
                tx.SetItem(NextSessionSequence());
            });
            if (setResult.IsFailed) {
                var errorMsg = string.Join("; ", setResult.Errors.Select(e => e.Message));
                logger.LogWarning(nameof(HandleRequestAddIdentifierRpcAsync) + ": Failed to update storage: {Errors}", errorMsg);
                await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                    result: new AddIdentifierResponse(Success: false, Error: $"Failed to update storage: {errorMsg}"));
                return;
            }

            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new AddIdentifierResponse(Success: true, Prefix: createResult.Value.Prefix));
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleRequestAddIdentifierRpcAsync) + ": Exception occurred");
            await _portService.SendRpcResponseAsync(portId, request.PortSessionId, request.Id,
                result: new AddIdentifierResponse(Success: false, Error: ex.Message));
        }
    }

    private async Task SignAndSendRequestHeaders(AppBwReplySignMessage msg,
        string? portId = null, string? portSessionId = null, string? rpcRequestId = null) {
        // Helper to send response via port
        async Task SendResponseAsync(object? result, string? error) {
            if (portId is not null && portSessionId is not null) {
                // Route via port
                await _portService.SendRpcResponseAsync(portId, portSessionId, rpcRequestId ?? msg.RequestId ?? "",
                    result: error is null ? result : null, errorMessage: error);
            }
            else {
                logger.LogWarning(nameof(SignAndSendRequestHeaders) + ": No port info for sign-headers response, requestId={RequestId}. Response not sent.", msg.RequestId);
            }
        }

        try {
            await EnsureInitializedAsync();

            var payload = msg.Payload;
            if (payload is null) {
                logger.LogError(nameof(SignAndSendRequestHeaders) + ": could not parse payload");
                await SendResponseAsync(null, $"{nameof(SignAndSendRequestHeaders)}: could not parse payload on msg: {msg}");
                return;
            }

            // Extract values from AppBwReplySignPayload2
            var origin = payload.Origin;
            var requestUrl = payload.Url;
            var method = payload.Method;
            var headers = payload.Headers;
            var prefix = payload.Prefix;

            logger.LogInformation(nameof(SignAndSendRequestHeaders) + ": origin={origin}, url={url}, method={method}, prefix={prefix}",
                origin, requestUrl, method, prefix);

            // Validate URL is well-formed
            if (string.IsNullOrEmpty(requestUrl) || !Uri.IsWellFormedUriString(requestUrl, UriKind.Absolute)) {
                logger.LogWarning(nameof(SignAndSendRequestHeaders) + ": URL is empty or not well-formed: {Url}", requestUrl);
                await SendResponseAsync(null, "URL is empty or not well-formed");
                return;
            }

            // Validate prefix is provided
            if (string.IsNullOrEmpty(prefix)) {
                logger.LogWarning(nameof(SignAndSendRequestHeaders) + ": no identifier prefix provided");
                await SendResponseAsync(null, "No identifier configured for signing");
                return;
            }

            // Get generated signed headers from signify-ts
            var headersDictJson = JsonSerializer.Serialize(headers);

            // Ensure SignifyClient is connected before signing
            var connectionResult = await EnsureSignifyConnectedAsync();
            if (connectionResult.IsFailed) {
                var errorMsg = connectionResult.Errors.Count > 0 ? connectionResult.Errors[0].Message : "SignifyClient not connected";
                logger.LogWarning(nameof(SignAndSendRequestHeaders) + ": SignifyClient not connected: {Error}", errorMsg);
                await SendResponseAsync(null, $"SignifyClient not connected: {errorMsg}");
                return;
            }

            var signedHeadersJson = await _signifyClientBinding.GetSignedHeadersAsync(
                origin,
                requestUrl,
                method,
                headersDictJson,
                aidNameOrPrefix: prefix
            );

            var signedHeaders = JsonSerializer.Deserialize<Dictionary<string, string>>(signedHeadersJson);
            if (signedHeaders == null) {
                logger.LogWarning(nameof(SignAndSendRequestHeaders) + ": failed to generate signed headers");
                await SendResponseAsync(null, "Failed to generate signed headers");
                return;
            }

            logger.LogInformation(nameof(SignAndSendRequestHeaders) + ": successfully generated signed headers");
            await SendResponseAsync(new { headers = signedHeaders }, null);
            return;
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(SignAndSendRequestHeaders) + ": error");
            await SendResponseAsync(null, $"{nameof(SignAndSendRequestHeaders)}: exception occurred.");
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
                logger.LogWarning(nameof(HandleCreateCredentialApprovalAsync) + ": No port info for create-credential response, requestId={RequestId}. Response not sent.", msg.RequestId);
            }
        }

        try {
            using var _bgSuspend = _broker.PrioritizeInteractive();
            logger.LogInformation(nameof(HandleCreateCredentialApprovalAsync) + ": Processing credential creation approval");

            // Deserialize the approval payload
            var payloadJson = JsonSerializer.Serialize(msg.Payload, JsonOptions.RecursiveDictionary);
            var approvalPayload = JsonSerializer.Deserialize<CreateCredentialApprovalPayload>(payloadJson, JsonOptions.RecursiveDictionary);

            if (approvalPayload is null) {
                logger.LogWarning(nameof(HandleCreateCredentialApprovalAsync) + ": Could not deserialize approval payload");
                await SendResponseAsync(null, "Invalid approval payload");
                return;
            }

            var aidName = approvalPayload.AidName;
            var aidPrefix = approvalPayload.AidPrefix;
            var schemaSaid = approvalPayload.SchemaSaid;

            logger.LogInformation(nameof(HandleCreateCredentialApprovalAsync) + ": aidName={AidName}, aidPrefix={AidPrefix}, schemaSaid={SchemaSaid}",
                aidName, aidPrefix, schemaSaid);

            // Get registry for this AID, creating one if none exists
            string registryId;
            var registriesResult = await _broker.EnqueueCommandAsync(SignifyOperation.ListRegistries,
                svc => svc.ListRegistries(aidPrefix));
            if (registriesResult.IsSuccess && registriesResult.Value.Count > 0) {
                registryId = registriesResult.Value[0].Regk;
            }
            else {
                logger.LogInformation(nameof(HandleCreateCredentialApprovalAsync) + ": no registry found for AID {AidPrefix}, creating one", aidPrefix);
                var registryName = $"reg-{aidPrefix[..8]}-{schemaSaid[..8]}";
                var createResult = await _broker.EnqueueCommandAsync(SignifyOperation.CreateRegistryIfNotExists,
                    svc => svc.CreateRegistryIfNotExists(aidPrefix, registryName));
                if (createResult.IsFailed) {
                    logger.LogWarning(nameof(HandleCreateCredentialApprovalAsync) + ": failed to create registry: {Error}", createResult.Errors[0].Message);
                    await SendResponseAsync(null, "Failed to create credential registry: " + createResult.Errors[0].Message);
                    return;
                }
                registryId = createResult.Value.Regk;
                logger.LogInformation(nameof(HandleCreateCredentialApprovalAsync) + ": created registry '{RegistryName}' with regk={Regk}", registryName, registryId);
            }

            if (string.IsNullOrEmpty(registryId)) {
                logger.LogWarning(nameof(HandleCreateCredentialApprovalAsync) + ": registry ID is empty for AID {AidPrefix}", aidPrefix);
                await SendResponseAsync(null, "Invalid registry configuration");
                return;
            }

            // Verify schema exists, and if not, try to load it via OOBI
            var schemaResolved = await EnsureSchemaResolvedAsync(schemaSaid, nameof(HandleCreateCredentialApprovalAsync));
            if (!schemaResolved) {
                await SendResponseAsync(null, $"Failed to load credential schema {schemaSaid}. Ensure the schema OOBI is accessible.");
                return;
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
                logger.LogWarning(nameof(HandleCreateCredentialApprovalAsync) + ": CredData has unexpected type: {Type}", approvalPayload.CredData?.GetType().Name);
                await SendResponseAsync(null, "Unexpected credential data format");
                return;
            }

            // Build credential data structure
            var credentialData = new CredentialData(
                I: aidPrefix,               // Issuer prefix
                Ri: registryId,             // Registry ID
                S: schemaSaid,              // Schema SAID
                A: credDataOrdered          // Credential attributes (order-preserved)
            );

            logger.LogInformation(nameof(HandleCreateCredentialApprovalAsync) + ": issuing credential for AID {AidPrefix} with schema {SchemaSaid}",
                aidPrefix, schemaSaid);

            // Issue the credential
            var issueResult = await _broker.EnqueueCommandAsync(SignifyOperation.IssueCredential,
                svc => svc.IssueCredential(aidPrefix, credentialData));
            if (issueResult.IsFailed) {
                logger.LogWarning(nameof(HandleCreateCredentialApprovalAsync) + ": failed to issue credential: {Error}",
                    issueResult.Errors[0].Message);
                await SendResponseAsync(null, $"Failed to issue credential: {issueResult.Errors[0].Message}");
                return;
            }

            var credential = issueResult.Value;
            logger.LogInformation(nameof(HandleCreateCredentialApprovalAsync) + ": successfully created credential");
            await RefreshCachedCredentialsAsync();

            // Transform IssueCredentialResult to polaris-web CreateCredentialResult format
            // Convert Serder.Ked (OrderedDictionary) to RecursiveDictionary for proper serialization
            var createCredentialResult = new {
                acdc = credential.Acdc.Ked,
                iss = credential.Iss.Ked,
                anc = credential.Anc.Ked,
                op = credential.Op
            };

            // Send credential back to ContentScript via port
            logger.LogInformation(nameof(HandleCreateCredentialApprovalAsync) + ": Sending create-credential response, requestId={RequestId}", msg.RequestId);
            await SendResponseAsync(createCredentialResult, null);
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(HandleCreateCredentialApprovalAsync) + ": error");
            await SendResponseAsync(null, "Failed to create credential: " + ex.Message);
        }
    }

    /// <summary>
    /// Get the identifier name for a given prefix from cached session storage.
    /// This avoids calling signifyClientService.GetIdentifiers() repeatedly.
    /// </summary>
    private async Task<Result<string>> GetIdentifierNameFromCacheAsync(string prefix) {
        var connectionInfoResult = await _storageGateway.GetItem<KeriaConnectionInfo>(StorageArea.Session);
        if (connectionInfoResult.IsFailed || connectionInfoResult.Value == null) {
            return Result.Fail<string>("No KERIA connection info found in session storage");
        }

        var cachedIdentifiers = await _storageGateway.GetItem<CachedIdentifiers>(StorageArea.Session);
        var identifiersList = cachedIdentifiers.IsSuccess ? cachedIdentifiers.Value?.IdentifiersList : null;
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
    /// Refresh the identifiers cache in session storage.
    /// Skipped if:
    /// - Signify client is not connected
    /// - A long signify operation is in progress
    /// - Identifiers were fetched within AppConfig.IdentifiersPollSkipThreshold (unless force=true)
    /// Called after operations that modify identifiers (e.g., rename, create).
    /// </summary>
    // TODO P2: Add periodic polling that is relevant for when a group AID (multisig) may have its state changed by others in the group.
    private async Task RefreshIdentifiersCache(bool force = false) {
        if (!_signifyClientService.IsConnected) {
            logger.LogDebug(nameof(RefreshIdentifiersCache) + ": Skipped — signify not connected");
            return;
        }
        if (!force && await IsWithinPollSkipThresholdAsync(
                ps => ps.IdentifiersLastFetchedUtc, AppConfig.IdentifiersPollSkipThreshold)) {
            logger.LogDebug(nameof(RefreshIdentifiersCache) + ": Skipped — within poll threshold");
            return;
        }
        try {
            var identifiersResult = await _broker.EnqueueBackgroundAsync(SignifyOperation.GetIdentifiers,
                svc => svc.GetIdentifiers());
            if (identifiersResult.IsSuccess && identifiersResult.Value is not null) {
                var ps2 = await GetCurrentPollingStateAsync();
                await _storageGateway.WriteTransaction(StorageArea.Session, tx => {
                    tx.SetItem(new CachedIdentifiers { IdentifiersList = [identifiersResult.Value] });
                    tx.SetItem(ps2 with { IdentifiersLastFetchedUtc = DateTime.UtcNow });
                    tx.SetItem(NextSessionSequence());
                });
                logger.LogInformation(nameof(RefreshIdentifiersCache) + ": Updated CachedIdentifiers in session storage");

                // Auto-select the first AID if none is selected yet
                await AutoSelectFirstPrefixIfNeeded(identifiersResult.Value);
            }
            else {
                logger.LogWarning(nameof(RefreshIdentifiersCache) + ": GetIdentifiers failed or returned null");
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(RefreshIdentifiersCache) + ": Exception during refresh");
        }
    }

    /// <summary>
    /// Updates a specific timestamp field on the PollingState session record.
    /// Reads current state, applies the update function, and writes back.
    /// </summary>
    private async Task UpdatePollingTimestampAsync(Func<PollingState, PollingState> update) {
        try {
            var current = await GetCurrentPollingStateAsync();
            await _storageGateway.WriteTransaction(StorageArea.Session, tx => {
                tx.SetItem(update(current));
                tx.SetItem(NextSessionSequence());
            });
        }
        catch (Exception ex) {
            logger.LogWarning(ex, nameof(UpdatePollingTimestampAsync) + ": Failed to update polling timestamp");
        }
    }

    private async Task<PollingState> GetCurrentPollingStateAsync() {
        var result = await _storageGateway.GetItem<PollingState>(StorageArea.Session);
        return result.IsSuccess && result.Value is not null ? result.Value : new PollingState();
    }

    private async Task AutoSelectFirstPrefixIfNeeded(Identifiers identifiers) {
        if (identifiers.Aids.Count == 0) return;

        var prefsResult = await _storageGateway.GetItem<Preferences>();
        if (!prefsResult.IsSuccess || prefsResult.Value is null) return;

        var selectedDigest = prefsResult.Value.SelectedKeriaConnectionDigest;
        if (string.IsNullOrEmpty(selectedDigest)) return;

        var configsResult = await _storageGateway.GetItem<KeriaConnectConfigs>();
        if (!configsResult.IsSuccess || configsResult.Value is null) return;
        if (!configsResult.Value.Configs.TryGetValue(selectedDigest, out var currentConfig)) return;

        // Only set if no SelectedPrefix yet
        if (!string.IsNullOrEmpty(currentConfig.SelectedPrefix)) return;

        var firstPrefix = identifiers.Aids[0].Prefix;
        logger.LogInformation(nameof(AutoSelectFirstPrefixIfNeeded) + ": No SelectedPrefix set, auto-selecting first AID: {Prefix}", firstPrefix);

        var updatedConfig = currentConfig with { SelectedPrefix = firstPrefix };
        var updatedConfigs = new Dictionary<string, KeriaConnectConfig>(configsResult.Value.Configs) {
            [selectedDigest] = updatedConfig
        };
        await _storageGateway.SetItem(configsResult.Value with { Configs = updatedConfigs });
    }

    /// <summary>
    /// Ensures SignifyClient is connected, attempting reconnection if necessary.
    /// Call this at the start of any RPC handler that requires SignifyClient.
    /// </summary>
    private async Task<Result> EnsureSignifyConnectedAsync() {
        // Try a lightweight operation to check connection
        var stateResult = await _broker.EnqueueReadAsync(SignifyOperation.GetState, svc => svc.GetState());
        if (stateResult.IsSuccess) {
            return Result.Ok();
        }

        // Check if it's a recoverable connection error
        if (stateResult.Errors.OfType<NotConnectedError>().Any()) {
            logger.LogInformation(nameof(EnsureSignifyConnectedAsync) + ": SignifyClient not connected - attempting reconnect");
            return await TryConnectSignifyClientAsync();
        }

        // Non-recoverable error
        return Result.Fail(stateResult.Errors);
    }

    /// <summary>
    /// Wrapper that ensures SignifyClient is connected before proceeding.
    /// Returns false and sends error response if connection fails.
    /// </summary>
    private async Task<bool> RequireSignifyConnectionAsync(string portId, string portSessionId, string requestId) {
        var result = await EnsureSignifyConnectedAsync();
        if (result.IsFailed) {
            var errorMsg = result.Errors.Count > 0 ? result.Errors[0].Message : "SignifyClient not connected";
            await _portService.SendRpcResponseAsync(portId, portSessionId, requestId, errorMessage: errorMsg);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Try to connect the BackgroundWorker's SignifyClient instance using stored credentials
    /// This is needed because BackgroundWorker has a separate Blazor runtime from the App (popup/tab)
    /// </summary>
    private async Task<Result> TryConnectSignifyClientAsync() {
        try {
            // Skip if already connected — a concurrent BootAndConnect (new agent flow)
            // may have already established the connection, and reconnecting would
            // destructively reset the JS _client to null.
            if (_signifyClientService.IsConnected) {
                logger.LogInformation(nameof(TryConnectSignifyClientAsync) + ": Already connected, skipping redundant connect");
                return Result.Ok();
            }

            // Get preferences to find the selected config digest
            var prefsResult = await _storageGateway.GetItem<Preferences>();
            if (prefsResult.IsFailed || prefsResult.Value == null) {
                return Result.Fail("Preferences not found");
            }
            var selectedDigest = prefsResult.Value.SelectedKeriaConnectionDigest;
            if (string.IsNullOrEmpty(selectedDigest)) {
                return Result.Fail("No KERIA configuration selected");
            }

            // Get KeriaConnectConfigs dictionary and look up the selected config
            var configsResult = await _storageGateway.GetItem<KeriaConnectConfigs>();
            if (configsResult.IsFailed || configsResult.Value == null || !configsResult.Value.IsStored) {
                return Result.Fail("No KERIA connection configurations found");
            }
            if (!configsResult.Value.Configs.TryGetValue(selectedDigest, out var config)) {
                return Result.Fail($"Selected KERIA configuration not found for digest {selectedDigest}");
            }

            if (string.IsNullOrEmpty(config.AdminUrl)) {
                return Result.Fail("Admin URL not configured");
            }

            /*
            if (string.IsNullOrEmpty(config.BootUrl)) {
                return Result.Fail("Boot URL not configured");
            }
            */

            // Retrieve passcode from memory (never from storage)
            var passcode = _sessionManager.GetPasscode();
            if (string.IsNullOrEmpty(passcode)) {
                return Result.Fail("Passcode not in memory — session is locked");
            }
            if (passcode.Length != 21) {
                return Result.Fail("Invalid passcode length");
            }

            // Cancel any active burst before connecting — Connect() resets the JS client,
            // and in-flight polling calls would hit "Client not connected"
            _notificationPollingCts?.Cancel();

            // Connect to KERIA
            logger.LogInformation(nameof(TryConnectSignifyClientAsync) + ": connecting to {AdminUrl} (IsConnected={IsConnected}, hasPendingConnect={HasPending})",
                config.AdminUrl, _signifyClientService.IsConnected, _pendingConnectTask is not null);
            Result<State> connectResult;
            using (_broker.PrioritizeInteractive()) {
                connectResult = await _broker.EnqueueCommandAsync(SignifyOperation.Connect,
                    svc => svc.Connect(
                        config.AdminUrl,
                        passcode,
                        config.BootUrl,
                        isBootForced: false  // Don't force boot - just connect
                    ));
            }
            if (connectResult.IsFailed) {
                return Result.Fail($"Failed to connect: {connectResult.Errors[0].Message}");
            }
            logger.LogInformation(nameof(TryConnectSignifyClientAsync) + ": connected successfully");

            // Start notification burst polling (cancel any previous burst first)
            RestartNotificationBurst();

            // Proactively cache credentials in session storage for App components to read directly
            await RefreshCachedCredentialsAsync();

            return Result.Ok();
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(TryConnectSignifyClientAsync) + ": exception");
            return Result.Fail($"Exception connecting to KERIA: {ex.Message}");
        }
    }

    /// <summary>
    /// Tracks the wall-clock time of the most recent burst start, used to coalesce rapid-succession
    /// restarts. See AppConfig.NotificationBurstRestartCooldown.
    /// </summary>
    private DateTime _lastBurstRestartUtc = DateTime.MinValue;

    /// <summary>
    /// Cancels any active notification burst and starts a new one.
    /// Creates/replaces the recurring notification poll alarm.
    /// Coalesces rapid-succession restarts: if a burst was started within
    /// AppConfig.NotificationBurstRestartCooldown AND is still active, the existing burst is reused.
    /// </summary>
    private void RestartNotificationBurst() {
        var now = DateTime.UtcNow;
        var burstActive = _notificationPollingCts is not null && !_notificationPollingCts.IsCancellationRequested;
        if (burstActive && (now - _lastBurstRestartUtc) < AppConfig.NotificationBurstRestartCooldown) {
            logger.LogDebug(nameof(RestartNotificationBurst) + ": Coalesced — burst active and within cooldown");
            return;
        }

        _notificationPollingCts?.Cancel();
        var cts = new CancellationTokenSource();
        _notificationPollingCts = cts;
        _lastBurstRestartUtc = now;
        _ = RunBurstAsync(cts);
        _ = EnsureNotificationPollAlarmAsync();
    }

    private async Task RunBurstAsync(CancellationTokenSource cts) {
        try {
            await _notificationPollingService.StartPollingAsync(cts.Token);
        }
        catch (OperationCanceledException) {
            // Expected when burst is canceled — not an error
        }
        catch (Exception ex) {
            logger.LogError(ex, nameof(RunBurstAsync) + ": Notification polling failed");
        }
        finally {
            // Null out CTS when burst completes (naturally or via error) so "is active" checks work correctly
            if (_notificationPollingCts == cts) {
                _notificationPollingCts = null;
            }
        }
    }

    private async Task EnsureNotificationPollAlarmAsync() {
        try {
            await WebExtensions.Alarms.Create(AppConfig.NotificationPollAlarmName, new AlarmInfo {
                PeriodInMinutes = AppConfig.NotificationPollAlarmPeriodMinutes
            });
        }
        catch (Exception ex) {
            logger.LogWarning(ex, nameof(EnsureNotificationPollAlarmAsync) + ": Failed to create notification poll alarm");
        }
    }

    /// <summary>
    /// Cancels notification polling if running.
    /// Called when session locks to stop polling against a disconnected signify client.
    /// </summary>
    private async Task CancelNotificationPollingAsync() {
        if (_notificationPollingCts is not null) {
            logger.LogInformation(nameof(CancelNotificationPollingAsync) + ": Canceling notification polling and clearing alarm");
            _notificationPollingCts.Cancel();
            _notificationPollingCts.Dispose();
            _notificationPollingCts = null;
        }
        try {
            await WebExtensions.Alarms.Clear(AppConfig.NotificationPollAlarmName);
        }
        catch (Exception ex) {
            logger.LogWarning(ex, nameof(CancelNotificationPollingAsync) + ": Failed to clear notification poll alarm");
        }
    }

    public void Dispose() {
        _notificationPollingCts?.Cancel();
        _notificationPollingCts?.Dispose();
        _bwReadyStateObserver?.Dispose();
        _sessionStateObserver?.Dispose();
        GC.SuppressFinalize(this);
    }
}

