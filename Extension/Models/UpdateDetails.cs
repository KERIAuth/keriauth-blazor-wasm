using System.Text.Json.Serialization;

namespace Extension.Models
{
    public record UpdateDetails
    {
        [JsonPropertyName("reason")]
        public string? Reason { get; init; }

        [JsonPropertyName("previousVersion")]
        public string? PreviousVersion { get; init; }

        [JsonPropertyName("currentVersion")]
        public string? CurrentVersion { get; init; }

        [JsonPropertyName("timestamp")]
        public string? Timestamp { get; init; }
    }
}
