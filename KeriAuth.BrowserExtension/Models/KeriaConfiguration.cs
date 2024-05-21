using FluentResults;
using KeriAuth.BrowserExtension.UI.Views.Credentials;
using KeriAuth.BrowserExtension.UI.Views.Identifiers;
using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public class KeriaConfiguration
    {
        [JsonConstructor]
        public KeriaConfiguration(string url)
        {
            Url = url;
        }

        [JsonConstructor]
        public KeriaConfiguration(string url, string passphrase)
        {
            Url = url;
            Passphrase = passphrase;
        }

        [JsonPropertyName("prefs")]
        public string Url { get; }


        [JsonPropertyName("pp")]
        public string? Passphrase { get; private set; }

        public Result<bool> SetPassphrase(string passphrase)
        {
            if (Passphrase == null)
            {
                this.Passphrase = passphrase;
                return Result.Ok(true);
            }
            return Result.Fail("Passphrase can only be set once");
        }
    }
}
