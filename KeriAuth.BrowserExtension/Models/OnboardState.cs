using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public class OnboardState
    {
        [JsonConstructor]
        public OnboardState(bool hasAcknowledgedInstall, bool hasAcknowledgedNewVersion, DateTime tosAgreedUtc, int tosAgreedHash, DateTime privacyAgreedUtc, int privacyAgreedHash, bool isInstallOnboarded)
        {
            HasAcknowledgedInstall = hasAcknowledgedInstall;
            HasAcknowledgedNewVersion = hasAcknowledgedNewVersion;
            TosAgreedUtc = tosAgreedUtc;
            TosAgreedHash = tosAgreedHash;
            PrivacyAgreedUtc = privacyAgreedUtc;
            PrivacyAgreedHash = privacyAgreedHash;
            IsInstallOnboarded = isInstallOnboarded;
        }

        [JsonPropertyName("hasAcknowledgedInstall")]
        public bool HasAcknowledgedInstall { get; init; }

        [JsonPropertyName("hasAcknowledgedNewVersion")]
        public bool HasAcknowledgedNewVersion { get; init; }

        [JsonPropertyName("tosAgreedUtc")]
        public DateTime TosAgreedUtc { get; init; }

        [JsonPropertyName("tosAgreedHash")]
        public int TosAgreedHash { get; init; }

        [JsonPropertyName("privacyAgreedUtc")]
        public DateTime PrivacyAgreedUtc { get; init; }

        [JsonPropertyName("privacyAgreedHash")]
        public int PrivacyAgreedHash { get; init; }

        [JsonPropertyName("isOnboarded")]
        public bool IsInstallOnboarded { get; init; }
    }
}
