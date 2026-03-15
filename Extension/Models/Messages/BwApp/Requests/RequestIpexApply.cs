using System.Text.Json.Serialization;
using Extension.Helper;

namespace Extension.Models.Messages.BwApp.Requests;

/// <summary>
/// Payload for BW→App IPEX Apply request.
/// Contains the schema and recipient details for the apply request,
/// plus the original ContentScript request details needed for response routing.
/// </summary>
public record RequestIpexApplyPayload(
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("schemaSaid")] string SchemaSaid,
    [property: JsonPropertyName("recipient")] string RecipientPrefix,
    [property: JsonPropertyName("isPresentation")] bool IsPresentation,
    [property: JsonPropertyName("attributes")] RecursiveDictionary? Attributes,
    [property: JsonPropertyName("tabId")] int TabId,
    [property: JsonPropertyName("tabUrl")] string? TabUrl = null,
    [property: JsonPropertyName("originalRequestId")] string? OriginalRequestId = null,
    [property: JsonPropertyName("originalType")] string? OriginalType = null
);

/// <summary>
/// Request from BackgroundWorker to App to show IPEX Apply UI.
/// User selects an identifier to use as the sender for the apply message.
/// </summary>
public record BwAppRequestIpexApplyMessage : BwAppMessage<RequestIpexApplyPayload> {
    public BwAppRequestIpexApplyMessage(string requestId, RequestIpexApplyPayload payload)
        : base(BwAppMessageType.RequestIpexApply, requestId, payload) { }
}
