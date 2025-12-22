using System.Text.Json.Serialization;

namespace Extension.Models {
    public record StoredPasskeys {
        /// <summary>
        /// A randomly-generated UUID that uniquely identifies this browser profile.
        /// Combined with PRF output during key derivation to ensure passkeys
        /// cannot be used in a different browser profile.
        /// Persists even when all passkeys are removed.
        /// </summary>
        [JsonPropertyName("profileId")]
        public string? ProfileId { get; init; }

        [JsonPropertyName("passkeys")]
        public List<StoredPasskey> Passkeys { get; init; } = [];
    }
}
