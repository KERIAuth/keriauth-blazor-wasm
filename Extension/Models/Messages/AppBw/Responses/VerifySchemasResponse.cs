using System.Text.Json.Serialization;
using Extension.Models.Messages.Common;

namespace Extension.Models.Messages.AppBw.Responses;

public record SchemaVerifyResult(
    [property: JsonPropertyName("said")] string Said,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("embeddedHash")] string? EmbeddedHash,
    [property: JsonPropertyName("fetchedHash")] string? FetchedHash,
    [property: JsonPropertyName("match")] bool Match,
    [property: JsonPropertyName("error")] string? Error = null
);

public record VerifySchemasResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("results")] List<SchemaVerifyResult>? Results = null,
    [property: JsonPropertyName("error")] string? Error = null
) : IResponseMessage {
    public string? ErrorMessage => Error;
}
