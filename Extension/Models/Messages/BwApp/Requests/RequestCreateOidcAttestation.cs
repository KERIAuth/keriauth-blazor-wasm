using System.Text.Json.Serialization;

namespace Extension.Models.Messages.BwApp.Requests;

/// <summary>
/// Payload for BW→App OIDC attestation creation request.
/// Mirrors the TypeScript CreateOidcAttestationRequest interface (CsBwRpcPayloads.ts),
/// plus the original ContentScript request details needed for response routing.
/// The eight OIDC fields are serialized verbatim into the issued ECR credential's
/// engagementContextRole as JSON, so the verifier's VC Bridge can correlate the
/// admitted ACDC back to its in-flight OIDC session.
/// </summary>
public record RequestCreateOidcAttestationPayload(
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("verifierOobi")] string VerifierOobi,
    [property: JsonPropertyName("verifierAid")] string VerifierAid,
    [property: JsonPropertyName("schemaSaid")] string SchemaSaid,
    [property: JsonPropertyName("requestorName")] string RequestorName,
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("dateTime")] string DateTime,
    [property: JsonPropertyName("emailAddress")] string EmailAddress,
    [property: JsonPropertyName("preferred_username")] string PreferredUsername,
    [property: JsonPropertyName("tabId")] int TabId,
    [property: JsonPropertyName("tabUrl")] string? TabUrl = null,
    [property: JsonPropertyName("originalRequestId")] string? OriginalRequestId = null,
    [property: JsonPropertyName("originalType")] string? OriginalType = null
);

/// <summary>
/// Request from BackgroundWorker to App to show the OIDC attestation approval UI.
/// User selects an identifier to use as the issuer/sender for the grant.
/// </summary>
public record BwAppRequestCreateOidcAttestationMessage : BwAppMessage<RequestCreateOidcAttestationPayload> {
    public BwAppRequestCreateOidcAttestationMessage(string requestId, RequestCreateOidcAttestationPayload payload)
        : base(BwAppMessageType.RequestCreateOidcAttestation, requestId, payload) { }
}
