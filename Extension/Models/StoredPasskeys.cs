using System.Text.Json.Serialization;
using Extension.Models.Storage;

namespace Extension.Models {
    public record StoredPasskeys : IStorageModel {
        [JsonPropertyName("Passkeys")]
        public List<StoredPasskey> Passkeys { get; init; } = [];

        [JsonPropertyName("IsStored")]
        public bool IsStored { get; init; }
    }
}
