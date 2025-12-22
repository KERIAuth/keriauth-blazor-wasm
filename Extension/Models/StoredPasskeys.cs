using System.Text.Json.Serialization;

namespace Extension.Models {
    public record RegisteredAuthenticators {
        /// <summary>
        /// A randomly-generated UUID that uniquely identifies this browser profile.
        /// Combined with PRF output during key derivation to ensure credentials
        /// cannot be used in a different browser profile.
        /// Persists even when all authenticators are removed.
        /// </summary>
        [JsonPropertyName("profileId")]
        public string? ProfileId { get; init; }

        [JsonPropertyName("authenticators")]
        public List<RegisteredAuthenticator> Authenticators { get; init; } = [];
    }
}
