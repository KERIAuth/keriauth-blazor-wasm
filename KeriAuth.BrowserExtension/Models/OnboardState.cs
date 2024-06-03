using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public record OnboardState
    {
        [JsonConstructor]
        public OnboardState(bool hasAcknowledgedInstall = false, bool hasAcknowledgedNewVersion = false, DateTime? tosAgreedUtc = null, int tosAgreedHash = 0, DateTime? privacyAgreedUtc = null, int privacyAgreedHash = 0)
        {
            HasAcknowledgedInstall = hasAcknowledgedInstall;
            HasAcknowledgedNewVersion = hasAcknowledgedNewVersion;
            TosAgreedUtc = tosAgreedUtc;
            TosAgreedHash = tosAgreedHash;
            PrivacyAgreedUtc = privacyAgreedUtc;
            PrivacyAgreedHash = privacyAgreedHash;
        }

        [JsonPropertyName("hasAcknowledgedInstall")]
        public bool HasAcknowledgedInstall { get; init; }

        [JsonPropertyName("hasAcknowledgedNewVersion")]
        public bool HasAcknowledgedNewVersion { get; init; }

        [JsonPropertyName("tosAgreedUtc")]
        public DateTime? TosAgreedUtc { get; init; }

        [JsonPropertyName("tosAgreedHash")]
        public int TosAgreedHash { get; init; }

        [JsonPropertyName("privacyAgreedUtc")]
        public DateTime? PrivacyAgreedUtc { get; init; }

        [JsonPropertyName("privacyAgreedHash")]
        public int PrivacyAgreedHash { get; init; }
        
        public bool IsInstallOnboarded()
        {
            if (!HasAcknowledgedInstall
                || !HasAcknowledgedNewVersion
                || TosAgreedUtc is null
                || TosAgreedHash == 0
                || PrivacyAgreedUtc is null
                || PrivacyAgreedHash == 0
            )
            {
                return false;
            }
            return true;
        }
    }
}
