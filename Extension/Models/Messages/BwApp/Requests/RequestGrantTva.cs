using System.Text.Json.Serialization;

namespace Extension.Models.Messages.BwApp.Requests;

/// <summary>
/// Payload for BW→App TVA grant approval request (Dign feature). The page-supplied subset
/// mirrors GrantTvaRequest in scripts/types/src/CsBwRpcPayloads.ts. The BW enriches the
/// inbound page params with the resolved VerifierAid, plus the standard origin/tab/route
/// metadata, before sending to the App.
/// </summary>
public record RequestGrantTvaPayload(
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("verifierOobi")] string VerifierOobi,
    [property: JsonPropertyName("verifierAid")] string VerifierAid,
    [property: JsonPropertyName("schemaSaid")] string SchemaSaid,
    [property: JsonPropertyName("requestorName")] string RequestorName,
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("dateTime")] string DateTime,
    [property: JsonPropertyName("emailAddress")] string EmailAddress,
    [property: JsonPropertyName("preferred_username")] string? PreferredUsername,
    [property: JsonPropertyName("tabId")] int TabId,
    [property: JsonPropertyName("tabUrl")] string? TabUrl = null,
    [property: JsonPropertyName("originalRequestId")] string? OriginalRequestId = null,
    [property: JsonPropertyName("originalType")] string? OriginalType = null
);

/// <summary>
/// Request from BackgroundWorker to App to show the TVA grant approval UI.
/// User selects an identifier to use as the issuer/sender for the grant.
/// </summary>
public record BwAppRequestGrantTvaMessage : BwAppMessage<RequestGrantTvaPayload> {
    public BwAppRequestGrantTvaMessage(string requestId, RequestGrantTvaPayload payload)
        : base(BwAppMessageType.RequestGrantTva, requestId, payload) { }
}
