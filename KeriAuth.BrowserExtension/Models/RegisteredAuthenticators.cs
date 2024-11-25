using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    // This must be kept in sync with the corresponding typescript interface IRegisteredAuthenticators.ts
    public record RegisteredAuthenticators
    {
        [JsonPropertyName("authenticators")]
        public List<RegisteredAuthenticator> Authenticators { get; init; } = [];
    }
}