using System.Text.Json.Serialization;

namespace Extension.Models {
    public record AuthorizeResult {
        [JsonPropertyName("credential")]
        public required AuthorizeResultCredential? ARCredential { get; init; }

        [JsonPropertyName("identifier")]
        public required AuthorizeResultIdentifier? ARIdentifier { get; init; }

        [JsonPropertyName("expiry")]
        public long Expiry { get; init; } = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
    }
}
