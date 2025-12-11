using System.Text.Json.Serialization;

namespace Extension.Models.Messages.BwApp.Requests;

/// <summary>
/// Payload for BW→App SelectAuthorize request.
/// Contains information about the requesting origin for user authorization,
/// plus the original ContentScript request details needed for response routing.
/// </summary>
/// <param name="Origin">The origin URL requesting authorization.</param>
/// <param name="TabId">The browser tab ID where the request originated.</param>
/// <param name="TabUrl">The full URL of the requesting tab.</param>
/// <param name="OriginalRequestId">The original requestId from ContentScript→BW message, needed to route response back to ContentScript.</param>
/// <param name="OriginalType">The original message type from ContentScript (e.g., "/signify/authorize/aid").</param>
/// <param name="OriginalPayload">The original payload from ContentScript, if needed by App UI.</param>
public record RequestSelectAuthorizePayload(
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("tabId")] int TabId,
    [property: JsonPropertyName("tabUrl")] string? TabUrl = null,
    [property: JsonPropertyName("originalRequestId")] string? OriginalRequestId = null,
    [property: JsonPropertyName("originalType")] string? OriginalType = null,
    [property: JsonPropertyName("originalPayload")] object? OriginalPayload = null
);

/// <summary>
/// Request from BackgroundWorker to App to show SelectAuthorize UI.
/// User selects an identifier to authorize for the requesting origin.
/// </summary>
public record BwAppRequestSelectAuthorizeMessage : BwAppMessage<RequestSelectAuthorizePayload> {
    public BwAppRequestSelectAuthorizeMessage(string requestId, RequestSelectAuthorizePayload payload)
        : base(BwAppMessageType.RequestSelectAuthorize, requestId, payload) { }
}
