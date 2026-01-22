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
using Extension.Models.Storage;
using Extension.Services;
using Extension.Services.JsBindings;
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

    // Cached JsonSerializerOptions for message deserialization
    private static readonly JsonSerializerOptions PortMessageJsonOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    // private static bool isInitialized;

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

    // services
    private readonly ILogger<BackgroundWorker> logger;
    private readonly IJSRuntime _jsRuntime;
    private readonly IJsRuntimeAdapter _jsRuntimeAdapter;
    private readonly IStorageService _storageService;
    private readonly ISignifyClientService _signifyClientService;
    private readonly IWebsiteConfigService _websiteConfigService;
    private readonly IBwAppMessagingService _bwAppMessagingService;
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
        WebExtensions.Runtime.OnMessage.AddListener(OnMessageAsync);
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

    private readonly ISignifyClientBinding _signifyClientBinding;
    private readonly SessionManager _sessionManager;
    private readonly ChromeSidePanel _chromeSidePanel;

    public BackgroundWorker(
        ILogger<BackgroundWorker> logger,
        IJSRuntime jsRuntime,
        IJsRuntimeAdapter jsRuntimeAdapter,
        IStorageService storageService,
        ISignifyClientService signifyService,
        ISignifyClientBinding signifyClientBinding,
        IWebsiteConfigService websiteConfigService,
        IBwAppMessagingService bwAppMessagingService,
        IPendingBwAppRequestService pendingBwAppRequestService,
        IDemo1Binding demo1Binding,
        ISchemaService schemaService,
        SessionManager sessionManager) {
        this.logger = logger;
        _jsRuntime = jsRuntime;
        _signifyClientBinding = signifyClientBinding;
        _demo1Binding = demo1Binding;
        _jsRuntimeAdapter = jsRuntimeAdapter;
        _storageService = storageService;
        _signifyClientService = signifyService;
        _websiteConfigService = websiteConfigService;
        _bwAppMessagingService = bwAppMessagingService;
        _pendingBwAppRequestService = pendingBwAppRequestService;
        _schemaService = schemaService;
        _webExtensionsApi = new WebExtensionsApi(_jsRuntimeAdapter);
        _sessionManager = sessionManager;
        _chromeSidePanel = new ChromeSidePanel(_jsRuntimeAdapter);
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

            InitializeIfNeeded();
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
                    logger.LogInformation("OnInstalledAsyncUnhandled install reason: {Reason}", details.Reason);
                    break;
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, "OnInstalledAsync: Error handling onInstalled event");
            throw;
        }
    }


    // Ensure initialization is performed only once
    [JSInvokable]
    public static void InitializeIfNeeded() {
        /*
        if (isInitialized) {
            logger.LogInformation("BackgroundWorker already initialized, skipping");
            return;
        }
        logger.LogInformation("BackgroundWorker initializing...");
        // TODO P2 Perform any necessary initialization tasks here
        // e.g., load settings, initialize services, etc.

        // reload javascript modules, such as signifyClient
        // _jsModuleLoader.LoadAllModulesAsync().AsTask().Wait();

        isInitialized = true;
        logger.LogInformation("BackgroundWorker initialization complete.");
        return;
        */
    }

    // onStartup fires when Chrome launches with a profile (not incognito) that has the extension installed
    // Typical use: Re-initialize or restore state, reconnect services, refresh caches.
    // Parameters: none
    [JSInvokable]
    public async Task OnStartupAsync() {
        try {
            logger.LogInformation("OnStartupAsync event handler called");
            logger.LogInformation("Browser startup detected - reinitializing background worker");

            // Clear BwReadyState first to prevent App from using stale flag
            await _storageService.RemoveItem<BwReadyState>(StorageArea.Session);

            InitializeIfNeeded();

            // Ensure skeleton storage records exist (may have been cleared or corrupted)
            await InitializeStorageDefaultsAsync();

            await _sessionManager.ExtendIfUnlockedAsync();

            // Signal to App that BackgroundWorker initialization is complete
            await SetBwReadyStateAsync();

            logger.LogInformation("Background worker reinitialized on browser startup");
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error handling onStartup event");
            throw;
        }
    }

    // onMessage fires when extension parts or external apps send messages.
    // Typical use: Coordination, data requests.
    // Returns: Response to send back to the message sender (or null)
    // The browser-polyfill supports Promise returns from onMessage handlers,
    // allowing the sender's SendMessage to receive this return value.
    // [JSInvokable]
    public async Task<object?> OnMessageAsync(object messageObj, WebExtensions.Net.Runtime.MessageSender sender) {
        try {
            logger.LogInformation("OnMessageAsync event handler called");

            InitializeIfNeeded();

            // SECURITY: Validate message origin to prevent subdomain attacks
            // Only messages from the extension itself or explicitly permitted origins are allowed
            var isValidSender = await ValidateMessageSenderAsync(sender, messageObj);
            if (!isValidSender) {
                logger.LogWarning("OnMessageAsync: Message from invalid sender ignored. Sender URL: {Url}, ID: {Id}", sender.Url, sender.Id);
                return null;
            }

            // Try to deserialize as InboundMessage
            var messageJson = JsonSerializer.Serialize(messageObj);
            logger.LogDebug("OnMessageAsync messageJson: {MessageJson}", messageJson);
            var inboundMsg = JsonSerializer.Deserialize<ToBwMessage>(messageJson, PortMessageJsonOptions);

            if (inboundMsg?.Type != null) {
                logger.LogInformation("Message received: {Type} from {Url}", inboundMsg.Type, sender.Url);

                // Check if message is from extension (App) or from content script
                var isFromExtension = sender.Url?.StartsWith($"chrome-extension://{WebExtensions.Runtime.Id}", StringComparison.OrdinalIgnoreCase) ?? false;

                // Messages from App
                if (isFromExtension) {
                    logger.LogInformation("App->BW msgJson: {j}", messageJson);

                    // Deserialize to non-generic AppBwMessage (has JsonElement? Payload for two-phase deserialization)
                    var baseAppMsg = JsonSerializer.Deserialize<AppBwMessage>(messageJson, PortMessageJsonOptions);
                    if (baseAppMsg is null || string.IsNullOrEmpty(baseAppMsg.Type)) {
                        logger.LogWarning("Failed to deserialize AppBwMessage or missing type");
                        await HandleUnknownMessageActionAsync("unknown");
                        return null;
                    }

                    // Validate message type is known
                    if (!AppBwMessageType.TryParse(baseAppMsg.Type, out _)) {
                        logger.LogWarning("Unknown AppBwMessageType: {Type}", baseAppMsg.Type);
                        await HandleUnknownMessageActionAsync(baseAppMsg.Type);
                        return null;
                    }

                    await _sessionManager.ExtendIfUnlockedAsync();

                    // Convert to typed message using RecursiveDictionaryJsonOptions for payload deserialization
                    var appMsg = baseAppMsg.ToTyped<object>(RecursiveDictionaryJsonOptions);

                    logger.LogInformation("AppMessage deserialized - Type: {Type}, TabId: {TabId}, TabUrl: {TabUrl}",
                        appMsg.Type, appMsg.TabId, appMsg.TabUrl);

                    var response = await HandleAppMessageAsync(appMsg);
                    return response;
                }
                else {
                    // Handle ContentScript messages (from web pages)
                    var contentScriptMsg = JsonSerializer.Deserialize<CsBwMessage>(messageJson, PortMessageJsonOptions);
                    if (contentScriptMsg != null) {
                        // Note: intentionally not extending SessionExpirationTimer here, to effectively prevent a malicious page sending messages via ContentScript from keeping the session alive
                        // await _sessionManager.ExtendIfUnlockedAsync();
                        await HandleContentScriptMessageAsync(contentScriptMsg, sender);
                        return null;
                    }
                }

                // Try to deserialize as RuntimeMessage (internal extension messages)
                var message = JsonSerializer.Deserialize<RuntimeMessage>(messageJson);

                if (message?.Action != null) {
                    logger.LogInformation("Runtime message received: {Action} from {SenderId}", message.Action, sender?.Id);

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
                    return null;
                }
            }
            else {
                logger.LogWarning("Failed to deserialize InboundMessage.Type {MessageJson}", messageJson);
                return null;
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error handling runtime message");
            return null;
        }

        return null;
    }

    // onAlarm fires at a scheduled interval/time.
    [JSInvokable]
    public async Task OnAlarmAsync(BrowserAlarm alarm) {
        try {
            logger.LogInformation("OnAlarmAsync: '{AlarmName}' fired", alarm.Name);
            InitializeIfNeeded();
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

            InitializeIfNeeded();

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

            InitializeIfNeeded();

            logger.LogInformation("Tab removed: {TabId}, WindowId: {WindowId}, WindowClosing: {WindowClosing}",
                tabId, removeInfo?.WindowId, removeInfo?.IsWindowClosing);

            // NOTE: No cleanup needed with runtime.sendMessage approach
            // All message handling is stateless - no connection tracking required
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
            InitializeIfNeeded();
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
            InitializeIfNeeded();
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
            // Clear BwReadyState first to prevent App from using stale flag
            await _storageService.RemoveItem<BwReadyState>(StorageArea.Session);

            InitializeIfNeeded();

            // Create skeleton storage records for Preferences and OnboardState
            // KeriaConnectConfig is NOT created here - it requires user-provided URLs
            await InitializeStorageDefaultsAsync();

            // Signal to App that BackgroundWorker initialization is complete
            await SetBwReadyStateAsync();

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
            InitializeIfNeeded();

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

            // Clear BwReadyState first to prevent App from using stale flag
            await _storageService.RemoveItem<BwReadyState>(StorageArea.Session);

            InitializeIfNeeded();

            // Ensure skeleton storage records exist (may need migration for new version)
            await InitializeStorageDefaultsAsync();

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

            // Signal to App that BackgroundWorker initialization is complete
            await SetBwReadyStateAsync();

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
            logger.LogInformation("Lock app message received - app should be locked");

            // The InactivityTimerService handles the actual locking logic
            logger.LogInformation("App lock request processed");
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

            // Send lock message to all connected tabs/apps
            try {
                await _webExtensionsApi.Runtime.SendMessage(new { action = LockAppAction });
                logger.LogInformation("Sent LockApp message due to system lock detection");
            }
            catch (Exception ex) {
                logger.LogInformation(ex, "Could not send LockApp message (expected if no pages open)");
            }
            return;
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error handling system lock detection");
            return;
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
    /// Sends a message to a ContentScript in a specific tab using runtime.sendMessage.
    /// </summary>
    /// <param name="tabId">The tab ID to send the message to</param>
    /// <param name="message">The message to send</param>
    private async Task SendMessageToTabAsync(int tabId, object message) {
        try {
            // Serialize with RecursiveDictionaryConverter to preserve field ordering for CESR/SAID
            var messageJson = JsonSerializer.Serialize(message, RecursiveDictionaryJsonOptions);
            logger.LogInformation("BW→CS (tab {TabId}): {Message}", tabId, messageJson);

            // Deserialize back to object for WebExtensions API while preserving ordering
            var messageToSend = JsonSerializer.Deserialize<object>(messageJson, RecursiveDictionaryJsonOptions);
            await WebExtensions.Tabs.SendMessage(tabId, messageToSend);
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error sending message to tab {TabId}", tabId);
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
    /// Handles messages from ContentScript sent via runtime.sendMessage.
    /// These may include polaris-web protocol messages and other messages from CS, and routes them appropriately.
    /// </summary>
    /// <param name="msg">The ContentScript message</param>
    /// <param name="sender">Message sender information</param>
    /// <returns>Response object to send back to ContentScript</returns>
    private async Task HandleContentScriptMessageAsync(CsBwMessage msg, WebExtensions.Net.Runtime.MessageSender? sender) {
        try {
            logger.LogInformation("BW←CS: {Type}", msg.Type);

            switch (msg.Type) {
                case CsInternalMessageTypes.CS_READY:
                    // ContentScript sends this message when it initializes to notify the service worker.
                    // The icon update is already handled by app.ts (JavaScript layer) which has its own
                    // chrome.runtime.onMessage listener that runs before this C# handler.
                    // We explicitly ignore it here - no C# processing needed.
                    logger.LogDebug("Ignoring cs-ready message from ContentScript (icon update handled by app.ts)");
                    return;

                case CsBwMessageTypes.INIT:
                    // TODO P3 ContentScript is initializing - send READY response?
                    break;

                case CsBwMessageTypes.SELECT_AUTHORIZE_CREDENTIAL:
                // TODO P2 Implement credential selection vs generic authorize
                case CsBwMessageTypes.SELECT_AUTHORIZE_AID:
                // TODO P2 Implement AID selection vs generic authorize
                case CsBwMessageTypes.AUTHORIZE:
                    var tabId = sender?.Tab?.Id ?? -1;
                    if (tabId == -1) {
                        logger.LogWarning("BW←CS: No tab ID found for authorize request");
                        return;
                    }
                    await HandleSelectAuthorizeAsync(msg, sender);
                    return;

                case CsBwMessageTypes.SIGN_REQUEST:
                    await HandleRequestSignHeadersAsync(msg, sender);
                    return;

                case CsBwMessageTypes.SIGNIFY_EXTENSION_CLIENT:
                    // Send the extension ID, although this may be redundantas ContentScript can get it via chrome.runtime.id
                    // TODO P2 Add REPLY?
                    /*
                    return new {
                        type = BwCsMessageTypes.REPLY,
                        requestId = msg.RequestId,
                        payload = new { extensionId = WebExtensions.Runtime.Id }
                    };
                    */
                    logger.LogWarning("BW←CS: {Type} not yet implemented", msg.Type);
                    return;

                case CsBwMessageTypes.CREATE_DATA_ATTESTATION:
                    await HandleCreateDataAttestationAsync(msg, sender);
                    return;

                case CsBwMessageTypes.SIGN_DATA:
                    await HandleSignDataAsync(msg, sender);
                    return;
                case CsBwMessageTypes.CLEAR_SESSION:
                case CsBwMessageTypes.CONFIGURE_VENDOR:
                case CsBwMessageTypes.GET_CREDENTIAL:
                case CsBwMessageTypes.GET_SESSION_INFO:
                case CsBwMessageTypes.SIGNIFY_EXTENSION:

                default:
                    logger.LogWarning("BW←CS: Unknown message type: {Type}", msg.Type);
                    return;
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error handling ContentScript message");
            return; // new ErrorReplyMessage(msg.RequestId, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles strongly-typed messages from App (popup/tab/sidepanel).
    /// These messages are typically replies that need to be forwarded to ContentScript.
    /// The App includes the TabId in the message so we know which ContentScript to forward to.
    ///
    /// IMPORTANT: Message type transformation happens here.
    /// App→BW message types (e.g., /KeriAuth/signify/replyCredential) are transformed to
    /// BW→CS message types (e.g., /signify/reply) to maintain separate contracts.
    /// </summary>
    /// <param name="msg">The strongly-typed AppBwMessage with TabId indicating target tab</param>
    /// <returns>Response object to send back to the App, or null for fire-and-forget messages</returns>
    private async Task<object?> HandleAppMessageAsync(AppBwMessage<object> msg) {
        try {
            logger.LogInformation("BW←App: Received message type {Type} from App with tabId {TabId} msg: {msg}", msg.Type, msg.TabId, msg);

            // Transform App message type to ContentScript message type
            // App uses /KeriAuth/signify/replyCredential or /KeriAuth/signify/replyAID
            // ContentScript expects /signify/reply with polaris-web AuthorizeResult payload
            string contentScriptMessageType;
            string? errorStr = null;
            object? transformedPayload = msg.Payload; // Default to original payload


            // TODO P1 tmp clear all pending requests
            logger.LogWarning("Clearing all pending BwApp requests (temporary)");
            await _storageService.RemoveItem<PendingBwAppRequests>(StorageArea.Session);

            switch (msg.Type) {
                case AppBwMessageType.Values.ReplyAid:
                case AppBwMessageType.Values.ReplyCredential:
                    contentScriptMessageType = BwCsMessageTypes.REPLY;
                    // Transform AuthorizeResult (with Aid) to BwCsAuthorizeResultPayload (with identifier)
                    // to conform to polaris-web AuthorizeResult interface
                    transformedPayload = TransformToPolariWebAuthorizeResult(msg.Payload);
                    break;
                case AppBwMessageType.Values.ReplyApprovedSignHeaders:
                    if (msg.Payload is null) {
                        logger.LogWarning("Payload is null for {t}: {msg}", msg.Type, msg);
                        return null;
                    }
                    if (msg.RequestId is null) {
                        logger.LogWarning("RequestId is null for {t}: {msg}", msg.Type, msg);
                        return null;
                    }
                    if (msg.TabUrl is null) {
                        logger.LogWarning("TabUrl is null for {t}: {msg}", msg.Type, msg);
                        return null;
                    }

                    try {
                        // Deserialize payload to AppBwReplySignPayload2
                        var payloadJson = JsonSerializer.Serialize(msg.Payload, RecursiveDictionaryJsonOptions);
                        var signPayload = JsonSerializer.Deserialize<AppBwReplySignPayload2>(payloadJson, RecursiveDictionaryJsonOptions);

                        if (signPayload is null) {
                            logger.LogWarning("Could not deserialize payload to AppBwReplySignPayload2: {payload}", msg.Payload);
                            return null;
                        }

                        logger.LogInformation("ReplyApprovedSignHeaders: origin={origin}, url={url}, method={method}, prefix={prefix}",
                            signPayload.Origin, signPayload.Url, signPayload.Method, signPayload.Prefix);

                        await SignAndSendRequestHeaders(msg.TabUrl, msg.TabId,
                            new AppBwReplySignMessage(msg.TabId, msg.TabUrl, msg.RequestId, signPayload.Origin, signPayload.Url, signPayload.Method, signPayload.Headers, signPayload.Prefix));
                    }
                    catch (Exception ex) {
                        logger.LogError(ex, "Error deserializing AppBwReplySignPayload2 from payload: {payload}", msg.Payload);
                    }
                    return null; // TODO P2 send error message back to Cs?
                case AppBwMessageType.Values.ReplySignData:
                    contentScriptMessageType = BwCsMessageTypes.REPLY;
                    // Transform SignDataResult to polaris-web format (already matches)
                    // SignDataResult { aid, items[] } -> same format expected by polaris-web
                    try {
                        var signDataPayloadJson = JsonSerializer.Serialize(msg.Payload, RecursiveDictionaryJsonOptions);
                        var signDataResult = JsonSerializer.Deserialize<SignDataResult>(signDataPayloadJson, RecursiveDictionaryJsonOptions);
                        if (signDataResult is null) {
                            logger.LogWarning("Could not deserialize payload to SignDataResult: {payload}", msg.Payload);
                            contentScriptMessageType = BwCsMessageTypes.REPLY_CANCELED;
                            errorStr = "Failed to process sign-data result";
                        }
                        else {
                            transformedPayload = signDataResult;
                            logger.LogInformation("BW←App: ReplySignData received, aid={Aid}, itemCount={Count}",
                                signDataResult.Aid, signDataResult.Items?.Length ?? 0);
                        }
                    }
                    catch (Exception ex) {
                        logger.LogError(ex, "Error deserializing SignDataResult from payload: {payload}", msg.Payload);
                        contentScriptMessageType = BwCsMessageTypes.REPLY_CANCELED;
                        errorStr = "Error processing sign-data result";
                    }
                    break;
                case AppBwMessageType.Values.ReplyCreateCredential:
                    // User approved credential creation - issue the credential via signify-ts
                    return await HandleCreateCredentialApprovalAsync(msg);
                case AppBwMessageType.Values.ReplyError:
                    contentScriptMessageType = BwCsMessageTypes.REPLY;
                    errorStr = "An error ocurred in the KERI Auth app";
                    break; // will forward
                case AppBwMessageType.Values.ReplyCanceled:
                    contentScriptMessageType = BwCsMessageTypes.REPLY_CANCELED;
                    errorStr = "User canceled or rejected request";
                    // TODO P2 need to look at contents of the following? AppBwReplyCanceledMessage: AppBwMessage
                    break; // will forward
                case AppBwMessageType.Values.AppClosed:
                    // Notify BwAppMessagingService to fail any pending requests
                    _bwAppMessagingService.HandleAppClosed(msg.TabId);
                    contentScriptMessageType = BwCsMessageTypes.REPLY_CANCELED; // sic
                    errorStr = "The KERI Auth app was closed";
                    break; // will forward
                case AppBwMessageType.Values.UserActivity:
                    logger.LogInformation("BW←App: USER_ACTIVITY message received, updating session expiration if applicable");
                    await _sessionManager.ExtendIfUnlockedAsync();
                    return null;
                case AppBwMessageType.Values.RequestAddIdentifier:
                    return await HandleRequestAddIdentifierAsync(msg);
                case AppBwMessageType.Values.ResponseToBwRequest:
                    // App is responding to a BW-initiated request
                    if (msg.RequestId is not null) {
                        logger.LogInformation("BW←App: received ResponseToBwRequest ... handling");
                        _bwAppMessagingService.HandleResponseFromApp(msg.RequestId, msg.Payload);
                    }
                    else {
                        logger.LogWarning("BW←App: ResponseToBwRequest received without requestId");
                    }
                    return null;
                default:
                    logger.LogWarning("BW←App: Unknown App message type {Type}, using as-is", msg.Type);
                    contentScriptMessageType = msg.Type;
                    break; // will forward
            }

            // Create OutboundMessage for forwarding to ContentScript
            var forwardMsg = new BwCsMessage(
                type: contentScriptMessageType,
                requestId: msg.RequestId,
                data: transformedPayload,
                error: errorStr
            );

            // Forward the message to ContentScript on the specified tab
            logger.LogInformation("BW→CS: Forwarding message type {Type} (transformed from {OriginalType}) to tab {TabId}",
                contentScriptMessageType, msg.Type, msg.TabId);
            await SendMessageToTabAsync(msg.TabId, forwardMsg);
            return null;
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error forwarding App message to ContentScript");
            return null;
        }
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
            var payloadJson = JsonSerializer.Serialize(payload, RecursiveDictionaryJsonOptions);
            var authorizeResult = JsonSerializer.Deserialize<AppBwAuthorizeResult>(payloadJson, RecursiveDictionaryJsonOptions);

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

            var payloadJson = JsonSerializer.Serialize(msg.Payload, RecursiveDictionaryJsonOptions);
            var payload = JsonSerializer.Deserialize<RequestAddIdentifierPayload>(payloadJson, RecursiveDictionaryJsonOptions);

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

    /// <summary>
    /// Handles SELECT_AUTHORIZE message via runtime.sendMessage instead of port.
    /// Creates a pending BW→App request and opens the action popup for user to select an identifier.
    /// </summary>
    private async Task HandleSelectAuthorizeAsync(CsBwMessage msg, WebExtensions.Net.Runtime.MessageSender? sender) {
        try {
            logger.LogInformation("BW HandleSelectAuthorize: {Message}", JsonSerializer.Serialize(msg));

            if (sender?.Tab?.Id == null) {
                logger.LogWarning("BW HandleSelectAuthorize: no tabId found");
                return;
            }

            var tabId = sender.Tab.Id.Value;
            var tabUrl = sender.Url;

            // Extract origin from tab URL
            string origin = "unknown";
            if (tabUrl is not null && Uri.TryCreate(tabUrl, UriKind.Absolute, out var originUri)) {
                origin = $"{originUri.Scheme}://{originUri.Host}";
                if (!originUri.IsDefaultPort) {
                    origin += $":{originUri.Port}";
                }
            }

            logger.LogInformation("BW HandleSelectAuthorize: tabId={TabId}, origin={Origin}", tabId, origin);

            // Create the payload with original CS request details for response routing
            var payload = new RequestSelectAuthorizePayload(
                Origin: origin,
                TabId: tabId,
                TabUrl: tabUrl,
                OriginalRequestId: msg.RequestId,
                OriginalType: msg.Type,
                OriginalPayload: msg.Payload
            );

            // Create pending request for App - use original requestId so it's returned in the reply
            var pendingRequest = new PendingBwAppRequest {
                RequestId = msg.RequestId ?? Guid.NewGuid().ToString(),
                Type = BwAppMessageType.Values.RequestSelectAuthorize,
                Payload = payload,
                CreatedAtUtc = DateTime.UtcNow,
                TabId = tabId,
                TabUrl = tabUrl
            };

            await UseSidePanelOrActionPopupAsync(pendingRequest);
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error in HandleSelectAuthorize");
        }
    }

    /// <summary>
    /// Handles sign request via runtime.sendMessage.
    /// Creates a pending BW→App request and opens the action popup for user to approve signing.
    /// </summary>
    private async Task HandleRequestSignHeadersAsync(CsBwMessage msg, WebExtensions.Net.Runtime.MessageSender? sender) {
        try {
            logger.LogInformation("BW HandleRequestSignHeadersAsync: {Message}", JsonSerializer.Serialize(msg));

            if (sender?.Tab?.Id == null) {
                logger.LogError("BW HandleRequestSignHeadersAsync: no tabId found");
                return; // don't have tabId to send error back to user/caller
            }

            int tabId = sender.Tab.Id.Value;
            var tabUrl = sender.Url;
            if (tabUrl is null) {
                logger.LogWarning("BW HandleRequestSignHeadersAsync: sender URL is null");
                return;
            }

            // Extract origin (scheme://host:port) from the tab's URL
            string origin = "";
            if (Uri.TryCreate(tabUrl, UriKind.Absolute, out var originUri)) {
                origin = $"{originUri.Scheme}://{originUri.Host}";
                if (!originUri.IsDefaultPort) {
                    origin += $":{originUri.Port}";
                }
            }
            // TODO P2 validate origin against allowed patterns / permissions
            logger.LogInformation("BW HandleRequestSignHeadersAsync: tabId={TabId}, origin={Origin}", tabId, origin);

            // Deserialize payload to SignRequestArgs
            var payloadJson = JsonSerializer.Serialize(msg.Payload);
            logger.LogDebug("BW HandleRequestSignHeadersAsync: payloadJson={PayloadJson}", payloadJson);
            var signRequestPayload = JsonSerializer.Deserialize<SignRequestArgs>(payloadJson, PortMessageJsonOptions);
            if (signRequestPayload == null) {
                logger.LogWarning("BW HandleRequestSignHeadersAsync: invalid payload");
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "Invalid payload"));
                return;
            }

            // Extract method and url from typed payload
            var method = signRequestPayload.Method ?? "GET";
            var requestUrl = signRequestPayload.Url;
            var headers = signRequestPayload.Headers;
            logger.LogInformation("BW HandleRequestSignHeadersAsync: method={Method}, url={Url}, headersCount={HeadersCount}",
                method, requestUrl, headers?.Count ?? 0);
            if (headers != null && headers.Count > 0) {
                foreach (var kvp in headers) {
                    logger.LogDebug("BW HandleRequestSignHeadersAsync: header[{Key}]={Value}", kvp.Key, kvp.Value);
                }
            }
            if (string.IsNullOrEmpty(requestUrl)) {
                logger.LogWarning("BW HandleRequestSignHeadersAsync: URL is empty");
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "URL must not be empty"));
                return;
            }

            // Get or create website config for this origin
            var getOrCreateWebsiteRes = await _websiteConfigService.GetOrCreateWebsiteConfig(new Uri(origin));
            if (getOrCreateWebsiteRes.IsFailed) {
                logger.LogWarning("BW HandleRequestSignHeadersAsync: failed to get or create website config for origin {Origin}: {Error}",
                    origin, string.Join("; ", getOrCreateWebsiteRes.Errors.Select(e => e.Message)));
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "Failed to get or create website config"));
                return;
            }
            WebsiteConfig websiteConfig = getOrCreateWebsiteRes.Value.websiteConfig1;
            string? rememberedPrefix = websiteConfig.RememberedPrefixOrNothing ?? null;
            if (rememberedPrefix is null) {
                logger.LogInformation("BW HandleRequestSignHeadersAsync: no identifier was configured for origin {Origin}, so using user's currently selected prefix", origin);

                var prefsResult = await _storageService.GetItem<Preferences>();

                rememberedPrefix = prefsResult.IsSuccess && prefsResult.Value is not null
                    ? prefsResult.Value.SelectedPrefix
                    : AppConfig.DefaultPreferences.SelectedPrefix;
                // TODO P1 check whether this is for only this origin or all origins?
                var res = await _websiteConfigService.Update(websiteConfig with { RememberedPrefixOrNothing = rememberedPrefix });
                if (res.IsFailed) {
                    logger.LogWarning("BW HandleRequestSignHeadersAsync: failed to update website config with remembered prefix for origin {Origin}: {Error}",
                        origin, string.Join("; ", res.Errors.Select(e => e.Message)));
                    await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "Failed to update website config with remembered prefix"));
                    return;
                }
            }

            // Check if auto-sign is enabled for safe HTTP methods
            var safeMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS" };
            bool isSafeMethod = safeMethods.Contains(method);
            bool isAutoSignEnabled = websiteConfig.IsAutoSignSafeHeaders;

            if (isSafeMethod && isAutoSignEnabled && !string.IsNullOrEmpty(rememberedPrefix)) {
                logger.LogInformation("BW HandleRequestSignHeadersAsync: auto-signing safe method {Method} for origin {Origin}", method, origin);

                // Create a synthetic AppBwReplySignMessage to reuse the signing logic
                var autoSignMessage = new AppBwReplySignMessage(
                    tabId,
                    tabUrl,
                    msg.RequestId ?? Guid.NewGuid().ToString(),
                    origin,
                    requestUrl,
                    method,
                    signRequestPayload.Headers ?? new Dictionary<string, string>(),
                    rememberedPrefix
                );

                await SignAndSendRequestHeaders(tabUrl, tabId, autoSignMessage);
                return;
            }

            // Create the payload with original CS request details for response routing
            var payload = new RequestSignHeadersPayload(
                Origin: origin,
                Url: requestUrl,
                Method: method,
                Headers: signRequestPayload.Headers ?? new Dictionary<string, string>(),
                TabId: tabId,
                TabUrl: tabUrl,
                OriginalRequestId: msg.RequestId,
                OriginalType: msg.Type,
                OriginalPayload: msg.Payload,
                RememberedPrefix: rememberedPrefix
            );

            // Create pending request for App - use original requestId so it's returned in the reply
            var pendingRequest = new PendingBwAppRequest {
                RequestId = msg.RequestId ?? Guid.NewGuid().ToString(),
                Type = BwAppMessageType.Values.RequestSignHeaders,
                Payload = payload,
                CreatedAtUtc = DateTime.UtcNow,
                TabId = tabId,
                TabUrl = tabUrl
            };

            await UseSidePanelOrActionPopupAsync(pendingRequest);
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error in HandleRequestSignHeadersAsync");
        }
    }


    private async Task SignAndSendRequestHeaders(string tabUrl, int tabId, AppBwReplySignMessage msg) {
        try {
            InitializeIfNeeded();

            var payload = msg.Payload;
            if (payload is null) {
                logger.LogError("SignAndSendRequestHeaders: could not parse payload");
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, $"SignAndSendRequestHeaders: could not parse payload on msg: {msg}"));
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
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "URL is empty or not well-formed"));
                return;
            }

            // Validate prefix is provided
            if (string.IsNullOrEmpty(prefix)) {
                logger.LogWarning("SignAndSendRequestHeaders: no identifier prefix provided");
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "No identifier configured for signing"));
                return;
            }

            // Get generated signed headers from signify-ts
            var headersDictJson = JsonSerializer.Serialize(headers);

            var readyRes = await _signifyClientService.Ready();
            if (readyRes.IsFailed) {
                logger.LogWarning("SignAndSendRequestHeaders: Signify client not ready: {Error}", readyRes.Errors[0].Message);
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, $"Signify client not ready: {readyRes.Errors[0].Message}"));
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
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "Failed to generate signed headers"));
                return;
            }

            logger.LogInformation("SignAndSendRequestHeaders: successfully generated signed headers");
            await SendMessageToTabAsync(tabId, new BwCsMessage(
                type: BwCsMessageTypes.REPLY,
                requestId: msg.RequestId,
                data: new { headers = signedHeaders },
                error: null
            ));
            return;
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error in SignAndSendRequestHeaders");
            await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, $"SignAndSendRequestHeaders: exception occurred."));
            return;
        }
    }

    /// <summary>
    /// Handles sign-data request from ContentScript.
    /// Creates a pending BW→App request and opens the popup for user to select an identifier and approve signing.
    /// </summary>
    private async Task HandleSignDataAsync(CsBwMessage msg, WebExtensions.Net.Runtime.MessageSender? sender) {
        try {
            logger.LogInformation("BW HandleSignData: {Message}", JsonSerializer.Serialize(msg));

            if (sender?.Tab?.Id == null) {
                logger.LogError("BW HandleSignData: no tabId found");
                return; // don't have tabId to send errorStr back
            }

            var tabId = sender.Tab.Id.Value;
            var tabUrl = sender.Url ?? "unknown";

            // Extract origin domain from URL
            string origin = tabUrl;
            if (Uri.TryCreate(tabUrl, UriKind.Absolute, out var originUri)) {
                origin = $"{originUri.Scheme}://{originUri.Host}";
                if (!originUri.IsDefaultPort) {
                    origin += $":{originUri.Port}";
                }
            }

            logger.LogInformation("BW HandleSignData: tabId={TabId}, origin={Origin}", tabId, origin);

            // Deserialize payload to SignDataArgs
            var payloadJson = JsonSerializer.Serialize(msg.Payload);
            var signDataArgs = JsonSerializer.Deserialize<SignDataArgs>(payloadJson, PortMessageJsonOptions);

            if (signDataArgs == null) {
                logger.LogWarning("BW HandleSignData: invalid payload");
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "Invalid payload"));
                return;
            }

            // Create the payload with original CS request details for response routing
            var requestPayload = new RequestSignDataPayload(
                Origin: origin,
                Message: signDataArgs.Message,
                Items: signDataArgs.Items,
                TabId: tabId,
                TabUrl: tabUrl,
                OriginalRequestId: msg.RequestId,
                OriginalType: msg.Type,
                OriginalPayload: msg.Payload
            );

            // Create pending request for App - use original requestId so it's returned in the reply
            var pendingRequest = new PendingBwAppRequest {
                RequestId = msg.RequestId ?? Guid.NewGuid().ToString(),
                Type = BwAppMessageType.Values.RequestSignData,
                Payload = requestPayload,
                CreatedAtUtc = DateTime.UtcNow,
                TabId = tabId,
                TabUrl = tabUrl
            };

            await UseSidePanelOrActionPopupAsync(pendingRequest);
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error in HandleSignData");
            var errorMsg = "BW HandleSignData: exception occurred";
            logger.LogWarning("{m}", errorMsg);
            if (sender?.Tab?.Id is not null) {
                var tabId = sender.Tab.Id.Value;
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, errorMsg));
            }
        }
    }

    /// <summary>
    /// Handles data attestation credential creation request from ContentScript.
    /// Creates a credential based on provided credData and schemaSaid.
    /// </summary>
    private async Task HandleCreateDataAttestationAsync(CsBwMessage msg, WebExtensions.Net.Runtime.MessageSender? sender) {
        try {
            logger.LogInformation("BW HandleCreateDataAttestation: {Message}", JsonSerializer.Serialize(msg));

            if (sender?.Tab?.Id == null) {
                logger.LogError("BW HandleCreateDataAttestation: no tabId found");
                return;
            }

            var tabId = sender.Tab.Id.Value;
            var tabUrl = sender.Url ?? "unknown";

            // Extract origin domain from URL
            string origin = tabUrl;
            if (Uri.TryCreate(tabUrl, UriKind.Absolute, out var originUri)) {
                origin = $"{originUri.Scheme}://{originUri.Host}";
                if (!originUri.IsDefaultPort) {
                    origin += $":{originUri.Port}";
                }
            }

            logger.LogInformation("BW HandleCreateDataAttestation: tabId={TabId}, origin={Origin}", tabId, origin);

            // Deserialize payload using specific type
            var payloadJson = JsonSerializer.Serialize(msg.Payload);
            var payload = JsonSerializer.Deserialize<CreateDataAttestationPayload>(payloadJson, PortMessageJsonOptions);

            if (payload == null) {
                logger.LogWarning("BW HandleCreateDataAttestation: invalid payload");
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "Invalid payload"));
                return;
            }

            // Validate required fields
            if (payload.CredData == null || payload.CredData.Count == 0) {
                logger.LogWarning("BW HandleCreateDataAttestation: credData is empty or missing");
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "Credential data not specified"));
                return;
            }

            if (string.IsNullOrEmpty(payload.SchemaSaid)) {
                logger.LogWarning("BW HandleCreateDataAttestation: schemaSaid is empty or missing");
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, "Schema SAID not specified"));
                return;
            }

            // Create the payload with original CS request details for response routing
            // Preserve credData as-is (object) to maintain CESR/SAID ordering
            var requestPayload = new RequestCreateCredentialPayload(
                Origin: origin,
                CredData: payload.CredData,
                SchemaSaid: payload.SchemaSaid,
                TabId: tabId,
                TabUrl: tabUrl,
                OriginalRequestId: msg.RequestId,
                OriginalType: msg.Type,
                OriginalPayload: msg.Payload
            );

            // Create pending request for App - use original requestId so it's returned in the reply
            var pendingRequest = new PendingBwAppRequest {
                RequestId = msg.RequestId ?? Guid.NewGuid().ToString(),
                Type = BwAppMessageType.Values.RequestCreateCredential,
                Payload = requestPayload,
                CreatedAtUtc = DateTime.UtcNow,
                TabId = tabId,
                TabUrl = tabUrl
            };

            await UseSidePanelOrActionPopupAsync(pendingRequest);
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error in HandleCreateDataAttestation");
            var msg2 = "BW HandleCreateDataAttestation: exception occurred";
            logger.LogWarning("{m}", msg2);
            if (sender?.Tab?.Id is not null) {
                var tabId = sender.Tab.Id.Value;
                await SendMessageToTabAsync(tabId, new ErrorReplyMessage(msg.RequestId, msg2));
            }

            return;
        }
    }

    /// <summary>
    /// Handles user approval of credential creation from App.
    /// Issues the credential using signify-ts and sends the result to ContentScript.
    /// </summary>
    private async Task<object?> HandleCreateCredentialApprovalAsync(AppBwMessage<object> msg) {
        try {
            logger.LogInformation("BW HandleCreateCredentialApproval: Processing credential creation approval");

            // Deserialize the approval payload
            var payloadJson = JsonSerializer.Serialize(msg.Payload, RecursiveDictionaryJsonOptions);
            var approvalPayload = JsonSerializer.Deserialize<CreateCredentialApprovalPayload>(payloadJson, RecursiveDictionaryJsonOptions);

            if (approvalPayload is null) {
                logger.LogWarning("BW HandleCreateCredentialApproval: Could not deserialize approval payload");
                await SendMessageToTabAsync(msg.TabId, new ErrorReplyMessage(msg.RequestId, "Invalid approval payload"));
                return null;
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
                await SendMessageToTabAsync(msg.TabId, new ErrorReplyMessage(msg.RequestId,
                    "No credential registry found for this identifier. Please create a registry first."));
                return null;
            }

            var registry = registriesResult.Value[0]; // Use first registry
            var registryId = registry.Regk;

            if (string.IsNullOrEmpty(registryId)) {
                logger.LogWarning("BW HandleCreateCredentialApproval: registry ID is empty for AID {AidName}", aidName);
                await SendMessageToTabAsync(msg.TabId, new ErrorReplyMessage(msg.RequestId, "Invalid registry configuration"));
                return null;
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
                var credDataJson = JsonSerializer.Serialize(approvalPayload.CredData, RecursiveDictionaryJsonOptions);
                var credDataDict = JsonSerializer.Deserialize<Dictionary<string, object>>(credDataJson, RecursiveDictionaryJsonOptions);
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
                await SendMessageToTabAsync(msg.TabId, new ErrorReplyMessage(msg.RequestId,
                    $"Failed to issue credential: {issueResult.Errors[0].Message}"));
                return null;
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

            // Send credential back to ContentScript
            await SendMessageToTabAsync(msg.TabId, new BwCsMessage(
                type: BwCsMessageTypes.REPLY,
                requestId: msg.RequestId,
                data: createCredentialResult,
                error: null
            ));

            return null;
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error in HandleCreateCredentialApproval");
            await SendMessageToTabAsync(msg.TabId, new ErrorReplyMessage(msg.RequestId,
                "Failed to create credential: " + ex.Message));
            return null;
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

