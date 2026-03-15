using System.Text.Json.Serialization;

namespace Extension.Models.Messages.BwApp.Requests;

/// <summary>
/// Payload for BW→App IPEX Agree request.
/// Contains the offer SAID and recipient for the agree request,
/// plus the original ContentScript request details needed for response routing.
/// </summary>
public record RequestIpexAgreePayload(
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("offerSaid")] string OfferSaid,
    [property: JsonPropertyName("recipient")] string RecipientPrefix,
    [property: JsonPropertyName("isPresentation")] bool IsPresentation,
    [property: JsonPropertyName("tabId")] int TabId,
    [property: JsonPropertyName("tabUrl")] string? TabUrl = null,
    [property: JsonPropertyName("originalRequestId")] string? OriginalRequestId = null,
    [property: JsonPropertyName("originalType")] string? OriginalType = null
);

/// <summary>
/// Request from BackgroundWorker to App to show IPEX Agree UI.
/// User selects an identifier to use as the sender for the agree message.
/// </summary>
public record BwAppRequestIpexAgreeMessage : BwAppMessage<RequestIpexAgreePayload> {
    public BwAppRequestIpexAgreeMessage(string requestId, RequestIpexAgreePayload payload)
        : base(BwAppMessageType.RequestIpexAgree, requestId, payload) { }
}
