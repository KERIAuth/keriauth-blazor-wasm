using System.Text.Json.Serialization;
using Extension.Helper;

namespace Extension.Services.SignifyService.Models {
    /// <summary>
    /// Result of createAidWithEndRole: AID creation + endRole + OOBI retrieval.
    /// </summary>
    public record AidWithOobi(
        [property: JsonPropertyName("prefix")] string Prefix,
        [property: JsonPropertyName("oobi")] string Oobi
    );

    /// <summary>
    /// Result of createDelegateAid: delegate AID creation.
    /// </summary>
    public record DelegateAidResult(
        [property: JsonPropertyName("prefix")] string Prefix,
        [property: JsonPropertyName("operationName")] string OperationName
    );

    /// <summary>
    /// Result of createRegistryIfNotExists: idempotent registry creation.
    /// </summary>
    public record RegistryCheckResult(
        [property: JsonPropertyName("regk")] string Regk,
        [property: JsonPropertyName("created")] bool Created
    );

    /// <summary>
    /// Args for issueAndGetCredential composite operation.
    /// </summary>
    public record IssueAndGetCredentialArgs(
        [property: JsonPropertyName("issuerAidName")] string IssuerAidName,
        [property: JsonPropertyName("registryName")] string RegistryName,
        [property: JsonPropertyName("schema")] string Schema,
        [property: JsonPropertyName("holderPrefix")] string HolderPrefix,
        [property: JsonPropertyName("credData")] RecursiveDictionary CredData,
        [property: JsonPropertyName("credEdge")] RecursiveDictionary? CredEdge = null,
        [property: JsonPropertyName("credRules")] RecursiveDictionary? CredRules = null
    );

    /// <summary>
    /// Args for ipexGrantAndSubmit composite operation.
    /// </summary>
    public record IpexGrantSubmitArgs(
        [property: JsonPropertyName("senderName")] string SenderName,
        [property: JsonPropertyName("recipient")] string Recipient,
        [property: JsonPropertyName("acdc")] RecursiveDictionary Acdc,
        [property: JsonPropertyName("anc")] RecursiveDictionary Anc,
        [property: JsonPropertyName("iss")] RecursiveDictionary Iss
    );

    /// <summary>
    /// Args for ipexAdmitAndSubmit composite operation.
    /// </summary>
    public record IpexAdmitSubmitArgs(
        [property: JsonPropertyName("senderName")] string SenderName,
        [property: JsonPropertyName("recipient")] string Recipient,
        [property: JsonPropertyName("grantSaid")] string GrantSaid,
        [property: JsonPropertyName("message")] string? Message = null
    );

    /// <summary>
    /// Args for ipexApplyAndSubmit composite operation.
    /// </summary>
    public record IpexApplySubmitArgs(
        [property: JsonPropertyName("senderName")] string SenderName,
        [property: JsonPropertyName("recipient")] string Recipient,
        [property: JsonPropertyName("schemaSaid")] string SchemaSaid,
        [property: JsonPropertyName("attributes")] RecursiveDictionary? Attributes = null
    );

    /// <summary>
    /// Args for ipexOfferAndSubmit composite operation.
    /// </summary>
    public record IpexOfferSubmitArgs(
        [property: JsonPropertyName("senderName")] string SenderName,
        [property: JsonPropertyName("recipient")] string Recipient,
        [property: JsonPropertyName("credentialSaid")] string CredentialSaid,
        [property: JsonPropertyName("applySaid")] string? ApplySaid = null
    );

    /// <summary>
    /// Args for ipexAgreeAndSubmit composite operation.
    /// </summary>
    public record IpexAgreeSubmitArgs(
        [property: JsonPropertyName("senderName")] string SenderName,
        [property: JsonPropertyName("recipient")] string Recipient,
        [property: JsonPropertyName("offerSaid")] string OfferSaid
    );
}
