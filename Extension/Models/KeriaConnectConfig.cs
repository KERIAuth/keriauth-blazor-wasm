using System.Globalization;
using System.Text.Json.Serialization;

namespace Extension.Models
{
    public record KeriaConnectConfig
    {
        [JsonConstructor]
        public KeriaConnectConfig(string? providerName = null, string? adminUrl = null, string? bootUrl = null, int passcodeHash = 0, string? clientAidPrefix = null, string? agentAidPrefix = null)
        {
            ProviderName = providerName;
#pragma warning disable CA1305 // Specify IFormatProvider
            Alias = "Created " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "UTC";
#pragma warning restore CA1305 // Specify IFormatProvider
            AdminUrl = adminUrl;
            BootUrl = bootUrl;
            PasscodeHash = passcodeHash;
            ClientAidPrefix = clientAidPrefix;
            AgentAidPrefix = agentAidPrefix;
        }

        [JsonPropertyName("AdminUrl")]
        public string? AdminUrl { get; init; }

        [JsonPropertyName("BootUrl")]
        public string? BootUrl { get; init; }

        [JsonPropertyName("ProviderName")]
        public string? ProviderName { get; init; }

        [JsonPropertyName("Alias")]
        public string? Alias { get; init; }

        [JsonPropertyName("PasscodeHash")]
        public int PasscodeHash { get; init; }

        [JsonPropertyName("ClientAidPrefix")]
        public string? ClientAidPrefix { get; init; }

        [JsonPropertyName("AgentAidPrefix")]
        public string? AgentAidPrefix { get; init; }

        public bool IsAdminUrlConfigured()
        {
            if (string.IsNullOrEmpty(Alias)
                || PasscodeHash == 0
                || string.IsNullOrEmpty(AdminUrl)
                || !(Uri.TryCreate(AdminUrl, UriKind.Absolute, out Uri? adminUriResult)
                      && (adminUriResult.Scheme == Uri.UriSchemeHttp || adminUriResult.Scheme == Uri.UriSchemeHttps))
                //|| string.IsNullOrEmpty(BootUrl)
                //|| !(Uri.TryCreate(AdminUrl, UriKind.Absolute, out Uri? bootUriResult)
                //      && (bootUriResult.Scheme == Uri.UriSchemeHttp || bootUriResult.Scheme == Uri.UriSchemeHttps))
                )
            {
                return false;
            }
            return true;
        }
    }
}
