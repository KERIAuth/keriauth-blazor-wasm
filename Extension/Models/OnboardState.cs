using System.Text.Json.Serialization;
using Extension.Models.Storage;

namespace Extension.Models {
    public record OnboardState : IStorageModel {
        [JsonPropertyName("IsWelcomed")]
        public bool IsWelcomed { get; init; }

        [JsonPropertyName("IsStored")]
        public bool IsStored { get; init; }

        [JsonPropertyName("AcknowledgedInstalledVersion")]
        public string? InstallVersionAcknowledged { get; init; }

        [JsonPropertyName("TosAgreedUtc")]
        public DateTime? TosAgreedUtc { get; init; }

        [JsonPropertyName("TosAgreedHash")]
        public int TosAgreedHash { get; init; }

        [JsonPropertyName("PrivacyAgreedUtc")]
        public DateTime? PrivacyAgreedUtc { get; init; }

        [JsonPropertyName("PrivacyAgreedHash")]
        public int PrivacyAgreedHash { get; init; }
    }
}
