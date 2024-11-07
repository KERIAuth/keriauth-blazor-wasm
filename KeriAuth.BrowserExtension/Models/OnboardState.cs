using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public record OnboardState
    {

        [JsonPropertyName("HasAcknowledgedInstall")]
        public bool HasAcknowledgedInstall { get; init; }

        [JsonPropertyName("HasAcknowledgedNewVersion")]
        public bool HasAcknowledgedNewVersion { get; init; }

        [JsonPropertyName("TosAgreedUtc")]
        public DateTime? TosAgreedUtc { get; init; }

        [JsonPropertyName("TosAgreedHash")]
        public int TosAgreedHash { get; init; }

        [JsonPropertyName("PrivacyAgreedUtc")]
        public DateTime? PrivacyAgreedUtc { get; init; }

        [JsonPropertyName("PrivacyAgreedHash")]
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