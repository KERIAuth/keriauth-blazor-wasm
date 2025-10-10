using System.Text.Json.Serialization;

namespace Extension.Models;

/// <summary>
/// Type definitions for browser extension event handler parameters
/// </summary>

// TODO P2: Replace redundant type definitions with WebExtensions.Net equivalents:
//   - OnInstalledDetails -> WebExtensions.Net.Runtime.OnInstalledEventCallbackDetails
//   - MessageSender -> WebExtensions.Net.Runtime.MessageSender
//   - Tab -> WebExtensions.Net.Tabs.Tab
//   - Alarm -> WebExtensions.Net.Alarms.Alarm
//   - TabRemoveInfo -> WebExtensions.Net.Tabs.RemoveInfo
//   - ActiveInfo -> WebExtensions.Net.Tabs.ActiveInfo
//   - HighlightInfo -> WebExtensions.Net.Tabs.CallbackHighlightInfo or HighlightHighlightInfo
//   - TabChangeInfo -> WebExtensions.Net.Tabs.ChangeInfo
// RuntimeMessage is application-specific and should remain.

// Runtime.OnInstalled event details
public record OnInstalledDetails(
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("previousVersion")] string? PreviousVersion = null,
    [property: JsonPropertyName("id")] string? Id = null
);

// Runtime.OnMessage sender information
public record MessageSender(
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("url")] string? Url = null,
    [property: JsonPropertyName("origin")] string? Origin = null,
    [property: JsonPropertyName("frameId")] int? FrameId = null,
    [property: JsonPropertyName("documentId")] string? DocumentId = null,
    [property: JsonPropertyName("tab")] Tab? Tab = null,
    [property: JsonPropertyName("tlsChannelId")] string? TlsChannelId = null
);

// Tab information
public record Tab(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("windowId")] int WindowId,
    [property: JsonPropertyName("highlighted")] bool Highlighted,
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("pinned")] bool Pinned,
    [property: JsonPropertyName("incognito")] bool Incognito,
    [property: JsonPropertyName("url")] string? Url = null,
    [property: JsonPropertyName("title")] string? Title = null,
    [property: JsonPropertyName("favIconUrl")] string? FavIconUrl = null,
    [property: JsonPropertyName("status")] string? Status = null,
    [property: JsonPropertyName("pendingUrl")] string? PendingUrl = null,
    [property: JsonPropertyName("openerTabId")] int? OpenerTabId = null,
    [property: JsonPropertyName("groupId")] int? GroupId = null
);

// Alarm information
public record Alarm(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("scheduledTime")] double ScheduledTime,
    [property: JsonPropertyName("periodInMinutes")] double? PeriodInMinutes = null
);

// Tab removal information
public record TabRemoveInfo(
    [property: JsonPropertyName("windowId")] int WindowId,
    [property: JsonPropertyName("isWindowClosing")] bool IsWindowClosing
);

// Generic message structure for runtime messages
public record RuntimeMessage(
    [property: JsonPropertyName("action")] string? Action = null,
    [property: JsonPropertyName("type")] string? Type = null,
    [property: JsonPropertyName("data")] object? Data = null,
    [property: JsonPropertyName("payload")] object? Payload = null
);

// Tabs.OnActivated event information
public record ActiveInfo(
    [property: JsonPropertyName("tabId")] int TabId,
    [property: JsonPropertyName("windowId")] int WindowId
);

// Tabs.OnHighlighted event information
public record HighlightInfo(
    [property: JsonPropertyName("tabIds")] int[] TabIds,
    [property: JsonPropertyName("windowId")] int WindowId
);

// Tabs.OnUpdated changeInfo
public record TabChangeInfo(
    [property: JsonPropertyName("status")] string? Status = null,
    [property: JsonPropertyName("url")] string? Url = null,
    [property: JsonPropertyName("title")] string? Title = null,
    [property: JsonPropertyName("favIconUrl")] string? FavIconUrl = null,
    [property: JsonPropertyName("pinned")] bool? Pinned = null,
    [property: JsonPropertyName("audible")] bool? Audible = null,
    [property: JsonPropertyName("discarded")] bool? Discarded = null,
    [property: JsonPropertyName("autoDiscardable")] bool? AutoDiscardable = null,
    [property: JsonPropertyName("mutedInfo")] object? MutedInfo = null,
    [property: JsonPropertyName("groupId")] int? GroupId = null
);