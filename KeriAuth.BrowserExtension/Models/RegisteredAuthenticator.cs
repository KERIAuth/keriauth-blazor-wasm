using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public record RegisteredAuthenticator
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("credential")]
        public required string CredentialBase64 { get; init; }  // The Credential ArrayBuffer in Base64 format

        [JsonPropertyName("encryptedPasscodeBase64")]
        public string? EncryptedPasscodeBase64 { get; init; }

        [JsonPropertyName("registeredUtc")]
        public required DateTime CreationTime { get; init; }

        [JsonPropertyName("lastUpdatedUtc")]
        public DateTime LastUpdatedUtc { get; init; } = DateTime.UtcNow;

    }
}
