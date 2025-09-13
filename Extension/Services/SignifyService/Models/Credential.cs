using System.Collections.Specialized;
using System.Text.Json.Serialization;

namespace Extension.Services.SignifyService.Models {
    public class CredentialSubject : Dictionary<string, object> {
        [JsonPropertyName("i")]
        public string? I { get; init; }

        [JsonPropertyName("dt")]
        public string? Dt { get; init; }

        [JsonPropertyName("u")]
        public string? U { get; init; }

        [JsonPropertyName("LEI")]
        public string? LEI { get; init; }

        [JsonPropertyName("personLegalName")]
        public string? PersonLegalName { get; init; }

        [JsonPropertyName("engagementContextRole")]
        public string? EngagementContextRole { get; init; }

        [JsonPropertyName("officialOrganizationalRole")]
        public string? OfficialOrganizationalRole { get; init; }
    }

    public record CredentialData(
        [property: JsonPropertyName("v")] string? V = null,
        [property: JsonPropertyName("d")] string? D = null,
        [property: JsonPropertyName("u")] string? U = null,
        [property: JsonPropertyName("i")] string? I = null,
        [property: JsonPropertyName("ri")] string? Ri = null,
        [property: JsonPropertyName("s")] string? S = null,
        [property: JsonPropertyName("a")] OrderedDictionary A = null!,
        [property: JsonPropertyName("e")] OrderedDictionary? E = null,
        [property: JsonPropertyName("r")] OrderedDictionary? R = null
    );

    public record IssueCredentialArgs(
        [property: JsonPropertyName("issuerName")] string IssuerName,
        [property: JsonPropertyName("registryId")] string RegistryId,
        [property: JsonPropertyName("schemaId")] string SchemaId,
        [property: JsonPropertyName("recipient")] string Recipient,
        [property: JsonPropertyName("credentialData")] CredentialData CredentialData,
        [property: JsonPropertyName("source")] OrderedDictionary? Source = null,
        [property: JsonPropertyName("rules")] OrderedDictionary? Rules = null,
        [property: JsonPropertyName("privacy")] bool? Privacy = null,
        [property: JsonPropertyName("datetime")] string? Datetime = null
    );

    public record IssueCredentialResult(
        [property: JsonPropertyName("acdc")] Serder Acdc,
        [property: JsonPropertyName("anc")] Serder Anc,
        [property: JsonPropertyName("iss")] Serder Iss,
        [property: JsonPropertyName("op")] Operation Op
    );

    public record CredentialState(
        [property: JsonPropertyName("vn")] List<int> Vn,
        [property: JsonPropertyName("i")] string I,
        [property: JsonPropertyName("s")] string S,
        [property: JsonPropertyName("d")] string D,
        [property: JsonPropertyName("ri")] string Ri,
        [property: JsonPropertyName("a")] CredentialAnchor A,
        [property: JsonPropertyName("dt")] string Dt,
        [property: JsonPropertyName("et")] string Et,
        [property: JsonPropertyName("ra")] CredentialRevocationAnchor? Ra = null
    );

    public record CredentialAnchor(
        [property: JsonPropertyName("s")] int S,
        [property: JsonPropertyName("d")] string D
    );

    public record CredentialRevocationAnchor(
        [property: JsonPropertyName("i")] string? I = null,
        [property: JsonPropertyName("s")] string? S = null,
        [property: JsonPropertyName("d")] string? D = null
    );

    public record PresentCredentialArgs(
        [property: JsonPropertyName("holderName")] string HolderName,
        [property: JsonPropertyName("recipient")] string Recipient,
        [property: JsonPropertyName("said")] string Said,
        [property: JsonPropertyName("include")] bool? Include = null,
        [property: JsonPropertyName("datetime")] string? Datetime = null
    );

    public record RevokeCredentialArgs(
        [property: JsonPropertyName("issuerName")] string IssuerName,
        [property: JsonPropertyName("registryId")] string RegistryId,
        [property: JsonPropertyName("said")] string Said,
        [property: JsonPropertyName("datetime")] string? Datetime = null
    );

    public record CredentialFilter(
        [property: JsonPropertyName("filter")] Dictionary<string, object> Filter,
        [property: JsonPropertyName("skip")] int? Skip = null,
        [property: JsonPropertyName("limit")] int? Limit = null
    );

    public record CredentialSchema(
        [property: JsonPropertyName("$id")] string Id,
        [property: JsonPropertyName("$schema")] string Schema,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("credentialType")] string CredentialType,
        [property: JsonPropertyName("properties")] OrderedDictionary Properties
    );
}
