using System.Text.Json.Serialization;
using Extension.Helper;

namespace Extension.Services.SignifyService.Models {
    /// <summary>
    /// Represents a credential schema definition.
    /// Matches signify-ts Schema type.
    /// </summary>
    public record Schema(
        [property: JsonPropertyName("$id")] string Id,
        [property: JsonPropertyName("$schema")] string SchemaUri,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("credentialType")] string? CredentialType = null,
        [property: JsonPropertyName("version")] string? Version = null,
        [property: JsonPropertyName("properties")] RecursiveDictionary? Properties = null
    );
}
