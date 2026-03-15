using System.Text.Json.Serialization;

namespace Extension.Models.Messages.BwApp.Requests;

/// <summary>
/// Payload for BW→App IPEX Admit request (webpage-initiated).
/// Contains the grant SAID and recipient for the admit request,
/// plus the original ContentScript request details needed for response routing.
/// </summary>
public record RequestIpexAdmitPayload(
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("grantSaid")] string GrantSaid,
    [property: JsonPropertyName("recipient")] string RecipientPrefix,
    [property: JsonPropertyName("isPresentation")] bool IsPresentation,
    [property: JsonPropertyName("tabId")] int TabId,
    [property: JsonPropertyName("tabUrl")] string? TabUrl = null,
    [property: JsonPropertyName("originalRequestId")] string? OriginalRequestId = null,
    [property: JsonPropertyName("originalType")] string? OriginalType = null
);

/// <summary>
/// Request from BackgroundWorker to App to show IPEX Admit UI (webpage-initiated).
/// User selects an identifier to use as the sender for the admit message.
/// </summary>
public record BwAppRequestIpexAdmitMessage : BwAppMessage<RequestIpexAdmitPayload> {
    public BwAppRequestIpexAdmitMessage(string requestId, RequestIpexAdmitPayload payload)
        : base(BwAppMessageType.RequestIpexAdmitFromPage, requestId, payload) { }
}
