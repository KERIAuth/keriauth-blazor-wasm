using FluentResults;
using KeriAuth.BrowserExtension.UI.Views.Credentials;
using KeriAuth.BrowserExtension.UI.Views.Identifiers;
using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public class KeriaConnectConfig
    {
        [JsonConstructor]
        public KeriaConnectConfig(string keriaConnectAlias, string adminUrl, string bootUrl, string passphraseHash)
        {
            KeriaConnectAlias = keriaConnectAlias;
            AdminUrl = adminUrl;
            BootUrl = bootUrl;
            PassphraseHash = passphraseHash;
        }

        [JsonPropertyName("keriaConnectAlias")]
        public string KeriaConnectAlias { get; init; }
        
        [JsonPropertyName("adminUrl")]
        public string AdminUrl { get; init; }

        [JsonPropertyName("bootUrl")]
        public string BootUrl { get; init; }

        [JsonPropertyName("ph")]
        public string? PassphraseHash { get; init; }

        public bool IsConfigured()
        {
            if (string.IsNullOrEmpty(KeriaConnectAlias) 
                || string.IsNullOrWhiteSpace(AdminUrl) 
                || Uri.TryCreate(AdminUrl, UriKind.Absolute, out Uri? adminUriResult)
                      && (adminUriResult.Scheme == Uri.UriSchemeHttp || adminUriResult.Scheme == Uri.UriSchemeHttps)
                || string.IsNullOrWhiteSpace(BootUrl)
                || Uri.TryCreate(AdminUrl, UriKind.Absolute, out Uri? bootUriResult)
                      && (bootUriResult.Scheme == Uri.UriSchemeHttp || bootUriResult.Scheme == Uri.UriSchemeHttps)
                || string.IsNullOrWhiteSpace(PassphraseHash))
            {
                return false;
            }
            return true;
        }
    }
}
