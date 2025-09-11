using System.Text.Json.Serialization;

namespace Extension.Models
{
    public record KeriaConnectConfig
    {
        [JsonConstructor]
        public KeriaConnectConfig(string? providerName = null, string? adminUrl = null, string? bootUrl = null, int passcodeHash = 0, string? controllerAid = null)
        {
            ProviderName = providerName;
            Alias = "Connection created " + DateTime.UtcNow.ToString("o");
            AdminUrl = adminUrl;
            BootUrl = bootUrl;
            PasscodeHash = passcodeHash;
            ControllerAid = controllerAid;
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

        [JsonPropertyName("ControllerAid")]
        public string? ControllerAid { get; init; }

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
