﻿using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public record KeriaConnectConfig
    {
        [JsonConstructor]
        public KeriaConnectConfig(string? keriaConnectAlias = null, string? adminUrl = null, string? bootUrl = null, int passcodeHash = 0)
        {
            KeriaConnectAlias = keriaConnectAlias;
            AdminUrl = adminUrl;
            BootUrl = bootUrl;
            PasscodeHash = passcodeHash;
        }

        [JsonPropertyName("AdminUrl")]
        public string? AdminUrl { get; init; }

        [JsonPropertyName("BootUrl")]
        public string? BootUrl { get; init; }

        [JsonPropertyName("KeriaConnectAlias")]
        public string? KeriaConnectAlias { get; init; }

        [JsonPropertyName("PasscodeHash")]
        public int PasscodeHash { get; init; }

        public bool IsAdminUrlConfigured()
        {
            if (string.IsNullOrEmpty(KeriaConnectAlias)
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
