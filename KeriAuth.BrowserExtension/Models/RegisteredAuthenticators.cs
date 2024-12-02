using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public record RegisteredAuthenticators
    {
        [JsonPropertyName("authenticators")]
        public List<RegisteredAuthenticator> Authenticators { get; init; } = [];
    }
}