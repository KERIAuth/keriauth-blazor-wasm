using Extension.Models.Messages.AppBw;
using Extension.Services;
using Extension.Services.Port;
using Extension.UI.Layouts;
using Microsoft.AspNetCore.Components;
using WebExtensions.Net;
using BrowserTab = WebExtensions.Net.Tabs.Tab;
using TabActiveInfo = WebExtensions.Net.Tabs.ActiveInfo;
namespace Extension.UI.Components;

/// <summary>
/// Base class for tab dialog pages that handle pending requests from BackgroundWorker.
/// Provides common state, services, and lifecycle management for request handling pages.
///
/// Features:
/// - Shared state for request tracking (PageRequestId, TabId, HasRepliedToPage, etc.)
/// - Common cancel/dispose patterns to ensure pages always respond to requests
/// - Helper methods for clearing pending requests and waiting for cache updates
/// - Inherits from AuthenticatedPageBase for render suppression during session timeout
///
/// Usage:
/// <code>
/// @inherits TabDialogPageBase
///
/// @code {
///     protected override async Task OnInitializedAsync() {
///         await base.OnInitializedAsync();
///         var payload = await InitializeFromPendingRequestAsync&lt;MyPayloadType&gt;(BwAppMessageType.Values.MyRequestType);
///         if (payload is null) return;
///         // Use payload...
///     }
/// }
/// </code>
/// </summary>
public abstract class TabDialogPageBase : AuthenticatedPageBase, IAsyncDisposable {
    #region Injected Services

    [Inject]
    protected IAppBwPortService AppBwPortService { get; set; } = default!;

    [Inject]
    protected IPendingBwAppRequestService PendingBwAppRequestService { get; set; } = default!;

    [Inject]
    protected AppCache AppCache { get; set; } = default!;

    [Inject]
    protected IWebExtensionsApi WebExtensionsApi { get; set; } = default!;

    [Inject]
    protected ILogger<TabDialogPageBase> Logger { get; set; } = default!;

    #endregion

    #region Cascading Parameters

    /// <summary>
    /// Reference to the DialogLayout for navigation (ReturnToPriorUI, ClosePopupAsync).
    /// </summary>
    [CascadingParameter]
    public DialogLayout? Layout { get; set; }

    #endregion

    #region Shared State

    /// <summary>
    /// The original requestId from the web page that initiated this request.
    /// This must be returned in replies so the page can correlate the response.
    /// </summary>
    protected string PageRequestId { get; set; } = "";

    /// <summary>
    /// The browser tab ID associated with this request.
    /// </summary>
    protected int TabId { get; set; } = -1;

    /// <summary>
    /// Tracks whether a reply (success or cancel) has been sent to the page.
    /// Used to avoid sending duplicate cancel messages on dispose.
    /// </summary>
    protected bool HasRepliedToPage { get; set; }

    /// <summary>
    /// The origin URL of the requesting page.
    /// </summary>
    protected string OriginStr { get; set; } = "unset";

    /// <summary>
    /// Controls conditional rendering - set to true after successful initialization.
    /// </summary>
    protected bool IsInitialized { get; set; }

    /// <summary>
    /// The browser tab info for display purposes.
    /// </summary>
    protected BrowserTab? ActiveTab { get; set; }

    /// <summary>
    /// Tracks whether we're currently processing a tab-change cancellation.
    /// Prevents re-entrancy if multiple tab events fire in quick succession.
    /// </summary>
    private bool _isCancelingDueToTabChange;

    /// <summary>
    /// Reference to the tab activation handler for cleanup in DisposeAsync.
    /// Only set when running in sidePanel context.
    /// </summary>
    private Action<TabActiveInfo>? _tabActivatedHandler;

    #endregion

    #region Lifecycle Methods

    /// <summary>
    /// Initializes base class for authentication-based render suppression.
    /// Derived classes should call base.OnInitializedAsync() first.
    /// </summary>
    protected override async Task OnInitializedAsync() {
        await base.OnInitializedAsync();
        InitializeAppCache(AppCache);
    }

    /// <summary>
    /// Ensures a cancel message is sent if the page is disposed without an explicit action.
    /// Derived classes should call base.DisposeAsync() if overriding.
    /// </summary>
    public virtual async ValueTask DisposeAsync() {
        // Remove tab activation listener if registered
        RemoveTabChangeListener();

        // If user closes popup without previously clicking an action button,
        // try to send a cancel reply to the page so it's not left waiting.
        // Note: If this fails (port disconnecting), BackgroundWorker.CleanupOrphanedRequestsAsync
        // will detect the orphaned pending request and send the cancel response.
        if (!HasRepliedToPage && TabId > 0 && !string.IsNullOrEmpty(PageRequestId)) {
            Logger.LogInformation(nameof(DisposeAsync) + ": Sending cancel reply for pageRequestId={PageRequestId} (popup closed without action)", PageRequestId);
            await SendCancelMessageAsync($"User closed {GetType().Name} dialog");
        }

        // Don't clear pending request here - let BackgroundWorker handle it.
        // If SendCancelMessageAsync succeeded, HandleAppReplyCanceledRpcAsync clears it.
        // If SendCancelMessageAsync failed, CleanupOrphanedRequestsAsync clears it.

        GC.SuppressFinalize(this);
    }

    #endregion

    #region Protected Helper Methods

    /// <summary>
    /// Initializes common state from a pending BwApp request.
    /// Validates the request type and extracts the payload.
    /// </summary>
    /// <typeparam name="TPayload">The expected payload type.</typeparam>
    /// <param name="expectedType">The expected BwAppMessageType value (e.g., BwAppMessageType.Values.RequestSignIn).</param>
    /// <returns>The deserialized payload, or null if validation fails.</returns>
    protected async Task<TPayload?> InitializeFromPendingRequestAsync<TPayload>(string expectedType) where TPayload : class {
        Logger.LogInformation(nameof(InitializeFromPendingRequestAsync) + ": expectedType={ExpectedType}", expectedType);

        // Get the pending request from storage (set by BackgroundWorker before opening popup)
        var pendingRequest = AppCache.NextPendingBwAppRequest;
        if (pendingRequest?.Type != expectedType) {
            Logger.LogError(nameof(InitializeFromPendingRequestAsync) + ": No pending {ExpectedType} request found, got {ActualType}",
                expectedType, pendingRequest?.Type ?? "null");
            return null;
        }

        OriginStr = pendingRequest.TabUrl ?? "unknown";
        var payload = pendingRequest.GetPayload<TPayload>();
        if (payload is null) {
            Logger.LogError(nameof(InitializeFromPendingRequestAsync) + ": Failed to deserialize pending request payload to {PayloadType}",
                typeof(TPayload).Name);
            return null;
        }

        // Extract common values from the pending request
        PageRequestId = pendingRequest.RequestId;
        TabId = pendingRequest.TabId ?? -1;

        Logger.LogInformation(nameof(InitializeFromPendingRequestAsync) + ": pageRequestId={PageRequestId}, tabId={TabId}, origin={Origin}",
            PageRequestId, TabId, OriginStr);

        // Set up tab activation listener for sidePanel context
        // When user switches to a different tab, cancel this request
        SetupTabChangeListener();

        return payload;
    }

    /// <summary>
    /// Loads the ActiveTab from the current TabId.
    /// Call this after TabId is set if you need tab information for display.
    /// </summary>
    protected async Task LoadActiveTabAsync() {
        if (TabId > 0) {
            ActiveTab = await WebExtensionsApi.Tabs.Get(TabId);
        }
    }

    /// <summary>
    /// Sends a cancel message to BackgroundWorker and sets HasRepliedToPage.
    /// </summary>
    /// <param name="reason">The reason for cancellation.</param>
    protected async Task SendCancelMessageAsync(string reason) {
        Logger.LogInformation(nameof(SendCancelMessageAsync) + ": Sending cancel for pageRequestId={PageRequestId}, reason={Reason}",
            PageRequestId, reason);

        try {
            var message = new AppBwReplyCanceledMessage(TabId, OriginStr, PageRequestId, reason);
            await AppBwPortService.SendToBackgroundWorkerAsync(message);
            HasRepliedToPage = true;
        }
        catch (Exception ex) {
            Logger.LogError(ex, nameof(SendCancelMessageAsync) + ": Failed to send cancel message to BackgroundWorker");
            // Still mark as replied to prevent duplicate attempts
            HasRepliedToPage = true;
        }
    }

    /// <summary>
    /// Clears the pending request from storage using the PageRequestId captured during initialization.
    /// This ensures we clear the correct request even if multiple requests are queued.
    /// </summary>
    protected async Task ClearPendingRequestAsync() {
        if (string.IsNullOrEmpty(PageRequestId)) {
            Logger.LogWarning(nameof(ClearPendingRequestAsync) + ": No pageRequestId available to clear");
            return;
        }

        Logger.LogInformation(nameof(ClearPendingRequestAsync) + ": Clearing pending request, pageRequestId={PageRequestId}", PageRequestId);
        var result = await PendingBwAppRequestService.RemoveRequestAsync(PageRequestId);
        if (result.IsFailed) {
            Logger.LogError(nameof(ClearPendingRequestAsync) + ": Failed to remove request - {Errors}",
                string.Join(", ", result.Errors.Select(e => e.Message)));
        }
    }

    /// <summary>
    /// Waits for AppCache to reflect the cleared pending request.
    /// Use this to prevent race conditions with Index.razor routing.
    /// </summary>
    /// <param name="timeoutMs">Maximum time to wait in milliseconds.</param>
    /// <returns>True if cache cleared within timeout, false otherwise.</returns>
    protected async Task<bool> WaitForAppCacheClearAsync(int timeoutMs = 3000) {
        var waitResult = await AppCache.WaitForAppCache([() => !AppCache.HasPendingBwAppRequests], timeoutMs, 100);
        if (!waitResult) {
            Logger.LogWarning(nameof(WaitForAppCacheClearAsync) + ": AppCache did not clear pending requests within timeout, proceeding anyway");
        }
        else {
            Logger.LogInformation(nameof(WaitForAppCacheClearAsync) + ": AppCache confirmed no pending requests");
        }
        return waitResult;
    }

    /// <summary>
    /// Signals the start of an action that may take time (Cancel, Approve, etc.).
    /// Shows a spinner overlay after 250ms if the action is still in progress.
    /// Call this at the beginning of action handlers.
    /// </summary>
    protected async Task BeginActionAsync() {
        if (Layout is not null) {
            await Layout.BeginActionAsync();
        }
    }

    /// <summary>
    /// Combined helper: sends cancel message, clears pending request, waits for cache, and returns to prior UI.
    /// Use this for Cancel button handlers or system-triggered cancellations (e.g., tab change).
    /// Automatically shows spinner if action takes > 250ms.
    /// </summary>
    /// <param name="reason">The reason for cancellation.</param>
    protected async Task CancelAndReturnAsync(string reason) {
        Logger.LogInformation(nameof(CancelAndReturnAsync) + ": Canceling pageRequestId={PageRequestId}, reason={Reason}", PageRequestId, reason);

        await BeginActionAsync();
        await SendCancelMessageAsync(reason);
        await ClearPendingRequestAsync();
        await WaitForAppCacheClearAsync();
        await ReturnToPriorUIAsync();
    }

    /// <summary>
    /// Returns to the prior UI context (closes popup or navigates to Index).
    /// </summary>
    protected async Task ReturnToPriorUIAsync() {
        if (Layout is not null) {
            await Layout.ReturnToPriorUI();
        }
        else {
            Logger.LogWarning(nameof(ReturnToPriorUIAsync) + ": Layout is null, cannot return to prior UI");
        }
    }

    /// <summary>
    /// Marks the request as replied (use after sending a successful response).
    /// Call this when the primary action completes successfully.
    /// </summary>
    protected void MarkAsReplied() {
        HasRepliedToPage = true;
    }

    #endregion

    #region Tab Change Detection (SidePanel only)

    /// <summary>
    /// Sets up a listener for tab activation events when running in sidePanel context.
    /// When the user switches to a different tab, the pending request is canceled.
    /// </summary>
    private void SetupTabChangeListener() {
        // Only set up tab change listener for sidePanel context
        // Popups are independent of tab focus, and extension tabs don't need this
        if (!App.IsInSidePanel) {
            Logger.LogDebug(nameof(SetupTabChangeListener) + ": Not in sidePanel context, skipping tab change listener");
            return;
        }

        if (TabId <= 0) {
            Logger.LogDebug(nameof(SetupTabChangeListener) + ": No valid TabId, skipping tab change listener");
            return;
        }

        Logger.LogInformation(nameof(SetupTabChangeListener) + ": Setting up tab activation listener for TabId={TabId}", TabId);

        _tabActivatedHandler = OnTabActivated;
        WebExtensionsApi.Tabs.OnActivated.AddListener(_tabActivatedHandler);
    }

    /// <summary>
    /// Handles tab activation events. Cancels the request if user switches to a different tab.
    /// </summary>
    private void OnTabActivated(TabActiveInfo activeInfo) {
        // Fire-and-forget async handler (WebExtensions.Net pattern)
        _ = OnTabActivatedAsync(activeInfo);
    }

    /// <summary>
    /// Async handler for tab activation. Cancels and returns if the active tab differs from this request's tab.
    /// </summary>
    private async Task OnTabActivatedAsync(TabActiveInfo activeInfo) {
        // Ignore if already replied, already canceling, or same tab
        if (HasRepliedToPage || _isCancelingDueToTabChange) {
            return;
        }

        if (activeInfo.TabId == TabId) {
            Logger.LogDebug(nameof(OnTabActivatedAsync) + ": Same tab activated (TabId={TabId}), no action needed", TabId);
            return;
        }

        Logger.LogInformation(
            nameof(OnTabActivatedAsync) + ": User switched from TabId={RequestTabId} to TabId={NewTabId}, canceling request",
            TabId, activeInfo.TabId);

        _isCancelingDueToTabChange = true;

        try {
            await CancelAndReturnAsync("Request canceled when user navigated to another tab");
        }
        catch (Exception ex) {
            Logger.LogError(ex, nameof(OnTabActivatedAsync) + ": Error during cancel and return");
        }
        finally {
            _isCancelingDueToTabChange = false;
        }
    }

    /// <summary>
    /// Removes the tab activation listener if one was registered.
    /// </summary>
    private void RemoveTabChangeListener() {
        if (_tabActivatedHandler is not null) {
            Logger.LogDebug(nameof(RemoveTabChangeListener) + ": Removing tab activation listener");
            try {
                WebExtensionsApi.Tabs.OnActivated.RemoveListener(_tabActivatedHandler);
            }
            catch (Exception ex) {
                // RemoveListener may fail if already removed or context is disposed
                Logger.LogDebug(ex, nameof(RemoveTabChangeListener) + ": Failed to remove listener (may already be removed)");
            }
            _tabActivatedHandler = null;
        }
    }

    #endregion
}
