using System.Text.Json.Serialization;

namespace Extension.Models.Messages.BwApp.Requests;

/// <summary>
/// Payload for BW→App CreateCredential (data attestation) request.
/// Contains the credential data and schema to be attested,
/// plus the original ContentScript request details needed for response routing.
/// </summary>
/// <param name="Origin">The origin URL requesting credential creation.</param>
/// <param name="CredData">The credential attributes to be attested (preserved as object for CESR/SAID ordering).</param>
/// <param name="SchemaSaid">The SAID of the schema for the credential.</param>
/// <param name="TabId">The browser tab ID where the request originated.</param>
/// <param name="TabUrl">The full URL of the requesting tab.</param>
/// <param name="OriginalRequestId">The original requestId from ContentScript→BW message, needed to route response back to ContentScript.</param>
/// <param name="OriginalType">The original message type from ContentScript (e.g., "/signify/credential/create/data-attestation").</param>
/// <param name="OriginalPayload">The original payload from ContentScript, if needed by App UI.</param>
public record RequestCreateCredentialPayload(
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("credData")] object CredData,
    [property: JsonPropertyName("schemaSaid")] string SchemaSaid,
    [property: JsonPropertyName("tabId")] int TabId,
    [property: JsonPropertyName("tabUrl")] string? TabUrl = null,
    [property: JsonPropertyName("originalRequestId")] string? OriginalRequestId = null,
    [property: JsonPropertyName("originalType")] string? OriginalType = null,
    [property: JsonPropertyName("originalPayload")] object? OriginalPayload = null
);

/// <summary>
/// Request from BackgroundWorker to App to show CreateCredential UI.
/// User selects an identifier and approves or rejects creating the data attestation credential.
/// </summary>
public record BwAppRequestCreateCredentialMessage : BwAppMessage<RequestCreateCredentialPayload> {
    public BwAppRequestCreateCredentialMessage(string requestId, RequestCreateCredentialPayload payload)
        : base(BwAppMessageType.RequestCreateCredential, requestId, payload) { }
}
