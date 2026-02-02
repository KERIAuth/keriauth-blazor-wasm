using System.Text.Json.Serialization;
using Extension.Models.Storage;

namespace Extension.Models {
    public record StoredPasskeys : IStorageModel {
        /// <summary>
        /// Created initially, derrived from passcode hash, client AID, and agent AID.
        /// Combined with PRF output during key derivation.
        /// Persists even when all passkeys are removed.
        /// </summary>
        [JsonPropertyName("ProfileId")]
        public string? ProfileId { get; init; }

        [JsonPropertyName("Passkeys")]
        public List<StoredPasskey> Passkeys { get; init; } = [];

        [JsonPropertyName("IsStored")]
        public bool IsStored { get; init; }
    }
}
