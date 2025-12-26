using System.Text.Json.Serialization;

namespace Extension.Models.Messages.BwApp.Requests;

/// <summary>
/// Payload for BW→App SignData request.
/// Contains the data items to be signed and the message from the requesting page,
/// plus the original ContentScript request details needed for response routing.
/// </summary>
/// <param name="Origin">The origin URL requesting signing.</param>
/// <param name="Message">Optional message from the requesting page to display to user.</param>
/// <param name="Items">The data items (strings) to be signed.</param>
/// <param name="TabId">The browser tab ID where the request originated.</param>
/// <param name="TabUrl">The full URL of the requesting tab.</param>
/// <param name="OriginalRequestId">The original requestId from ContentScript→BW message, needed to route response back to ContentScript.</param>
/// <param name="OriginalType">The original message type from ContentScript (e.g., "/signify/sign-data").</param>
/// <param name="OriginalPayload">The original payload from ContentScript, if needed by App UI.</param>
public record RequestSignDataPayload(
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("items")] string[] Items,
    [property: JsonPropertyName("tabId")] int TabId,
    [property: JsonPropertyName("tabUrl")] string? TabUrl = null,
    [property: JsonPropertyName("originalRequestId")] string? OriginalRequestId = null,
    [property: JsonPropertyName("originalType")] string? OriginalType = null,
    [property: JsonPropertyName("originalPayload")] object? OriginalPayload = null
);

/// <summary>
/// Request from BackgroundWorker to App to show SignData UI.
/// User selects an identifier (and optionally credential) to sign the data items.
/// </summary>
public record BwAppRequestSignDataMessage : BwAppMessage<RequestSignDataPayload> {
    public BwAppRequestSignDataMessage(string requestId, RequestSignDataPayload payload)
        : base(BwAppMessageType.RequestSignData, requestId, payload) { }
}
