using Extension.Models.Messages.AppBw;
using Extension.Models.Messages.BwApp;
using Extension.Models.Storage;
using Extension.Services;
using Extension.Services.Port;
using Extension.UI.Layouts;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using WebExtensions.Net;
using WebExtensions.Net.Tabs;
using BrowserTab = WebExtensions.Net.Tabs.Tab;

namespace Extension.UI.Components;

/// <summary>
/// Base class for dialog pages that handle pending requests from BackgroundWorker.
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
/// @inherits DialogPageBase
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
public abstract class DialogPageBase : AuthenticatedPageBase, IAsyncDisposable {
    #region Injected Services

    [Inject]
    protected IAppPortService AppPortService { get; set; } = default!;

    [Inject]
    protected IPendingBwAppRequestService PendingBwAppRequestService { get; set; } = default!;

    [Inject]
    protected AppCache AppCache { get; set; } = default!;

    [Inject]
    protected IWebExtensionsApi WebExtensionsApi { get; set; } = default!;

    [Inject]
    protected ILogger<DialogPageBase> Logger { get; set; } = default!;

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
        // If user closes popup without previously clicking an action button,
        // try to send a cancel reply to the page so it's not left waiting.
        // Note: If this fails (port disconnecting), BackgroundWorker.CleanupOrphanedRequestsAsync
        // will detect the orphaned pending request and send the cancel response.
        if (!HasRepliedToPage && TabId > 0 && !string.IsNullOrEmpty(PageRequestId)) {
            Logger.LogInformation("DisposeAsync: Sending cancel reply for pageRequestId={PageRequestId} (popup closed without action)", PageRequestId);
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
        Logger.LogInformation("InitializeFromPendingRequestAsync: expectedType={ExpectedType}", expectedType);

        // Get the pending request from storage (set by BackgroundWorker before opening popup)
        var pendingRequest = AppCache.NextPendingBwAppRequest;
        if (pendingRequest?.Type != expectedType) {
            Logger.LogError("InitializeFromPendingRequestAsync: No pending {ExpectedType} request found, got {ActualType}",
                expectedType, pendingRequest?.Type ?? "null");
            return null;
        }

        OriginStr = pendingRequest.TabUrl ?? "unknown";
        var payload = pendingRequest.GetPayload<TPayload>();
        if (payload is null) {
            Logger.LogError("InitializeFromPendingRequestAsync: Failed to deserialize pending request payload to {PayloadType}",
                typeof(TPayload).Name);
            return null;
        }

        // Extract common values from the pending request
        PageRequestId = pendingRequest.RequestId;
        TabId = pendingRequest.TabId ?? -1;

        Logger.LogInformation("InitializeFromPendingRequestAsync: pageRequestId={PageRequestId}, tabId={TabId}, origin={Origin}",
            PageRequestId, TabId, OriginStr);

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
        Logger.LogInformation("SendCancelMessageAsync: Sending cancel for pageRequestId={PageRequestId}, reason={Reason}",
            PageRequestId, reason);

        try {
            var message = new AppBwReplyCanceledMessage(TabId, OriginStr, PageRequestId, reason);
            await AppPortService.SendToBackgroundWorkerAsync(message);
            HasRepliedToPage = true;
        }
        catch (Exception ex) {
            Logger.LogError(ex, "SendCancelMessageAsync: Failed to send cancel message to BackgroundWorker");
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
            Logger.LogWarning("ClearPendingRequestAsync: No pageRequestId available to clear");
            return;
        }

        Logger.LogInformation("ClearPendingRequestAsync: Clearing pending request, pageRequestId={PageRequestId}", PageRequestId);
        var result = await PendingBwAppRequestService.RemoveRequestAsync(PageRequestId);
        if (result.IsFailed) {
            Logger.LogError("ClearPendingRequestAsync: Failed to remove request - {Errors}",
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
            Logger.LogWarning("WaitForAppCacheClearAsync: AppCache did not clear pending requests within timeout, proceeding anyway");
        }
        else {
            Logger.LogInformation("WaitForAppCacheClearAsync: AppCache confirmed no pending requests");
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
    /// Use this for Cancel button handlers. Automatically shows spinner if action takes > 250ms.
    /// </summary>
    /// <param name="reason">The reason for cancellation.</param>
    protected async Task CancelAndReturnAsync(string reason) {
        Logger.LogInformation("CancelAndReturnAsync: User initiated cancel for pageRequestId={PageRequestId}", PageRequestId);

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
            Logger.LogWarning("ReturnToPriorUIAsync: Layout is null, cannot return to prior UI");
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
}
