using System.Text.Json.Serialization;

namespace Extension.Services.SignifyService.Models {
    public record Operation(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("metadata")] OperationMetadata? Metadata = null,
        [property: JsonPropertyName("done")] bool? Done = null,
        [property: JsonPropertyName("error")] object? Error = null,
        [property: JsonPropertyName("response")] object? Response = null
    );

    public record Operation<T>(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("metadata")] OperationMetadata? Metadata = null,
        [property: JsonPropertyName("done")] bool? Done = null,
        [property: JsonPropertyName("error")] object? Error = null,
        [property: JsonPropertyName("response")] T? Response = default
    );

    public class OperationMetadata : Dictionary<string, object> {
        [JsonPropertyName("depends")]
        public Operation? Depends { get; init; }

        [JsonPropertyName("sn")]
        public int? Sn { get; init; }

        [JsonPropertyName("anchor")]
        public string? Anchor { get; init; }
    }

    public record LongRunningOperationResult<T>(
        [property: JsonPropertyName("op")] Operation<T> Op,
        [property: JsonPropertyName("serder")] Serder? Serder = null,
        [property: JsonPropertyName("sigs")] List<string>? Sigs = null,
        [property: JsonPropertyName("atc")] string? Atc = null
    );

    public record OperationStatus(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("done")] bool Done,
        [property: JsonPropertyName("error")] string? Error = null,
        [property: JsonPropertyName("metadata")] Dictionary<string, object>? Metadata = null
    );

    public record OperationFilter(
        [property: JsonPropertyName("type")] string? Type = null,
        [property: JsonPropertyName("done")] bool? Done = null,
        [property: JsonPropertyName("name")] string? Name = null,
        [property: JsonPropertyName("limit")] int? Limit = null,
        [property: JsonPropertyName("skip")] int? Skip = null
    );
}
