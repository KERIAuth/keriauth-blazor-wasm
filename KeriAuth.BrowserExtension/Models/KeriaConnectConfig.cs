using KeriAuth.BrowserExtension.Services;
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
            PasscodeHash = passphraseHash;
        }

        [JsonPropertyName("keriaConnectAlias")]
        public string KeriaConnectAlias { get; init; }

        [JsonPropertyName("adminUrl")]
        public string AdminUrl { get; init; }

        [JsonPropertyName("bootUrl")]
        public string BootUrl { get; init; }

        [JsonPropertyName("ph")]
        public string? PasscodeHash { get; init; }

        public bool IsConfigured(IStateService.States currentAppState)
        {
            if ( currentAppState == IStateService.States.AuthenticatedConnected 
                || currentAppState == IStateService.States.AuthenticatedDisconnected
                || currentAppState == IStateService.States.Unauthenticated
                )
            {
                return true;
            }
            if (string.IsNullOrEmpty(KeriaConnectAlias)
                || string.IsNullOrWhiteSpace(PasscodeHash)
                || PasscodeHash == "0"
                || PasscodeHash == ""
                || string.IsNullOrEmpty(AdminUrl)
                || !(Uri.TryCreate(AdminUrl, UriKind.Absolute, out Uri? adminUriResult)
                      && (adminUriResult.Scheme == Uri.UriSchemeHttp || adminUriResult.Scheme == Uri.UriSchemeHttps))
                || string.IsNullOrEmpty(BootUrl)
                || !(Uri.TryCreate(AdminUrl, UriKind.Absolute, out Uri? bootUriResult)
                      && (bootUriResult.Scheme == Uri.UriSchemeHttp || bootUriResult.Scheme == Uri.UriSchemeHttps))
                )
            {
                return false;
            }
            return true;
        }
    }
}
