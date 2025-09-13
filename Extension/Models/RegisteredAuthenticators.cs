using System.Text.Json.Serialization;

namespace Extension.Models {
    public record RegisteredAuthenticators {
        [JsonPropertyName("authenticators")]
        public List<RegisteredAuthenticator> Authenticators { get; init; } = [];
    }
}
