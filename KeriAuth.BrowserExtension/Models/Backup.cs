using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public class Backup
    {
        [JsonPropertyName("WALLETENCRYPTED")]
        [NotNull]
        public WalletEncrypted? WalletEncrypted { get; set; }

        [JsonPropertyName("PREFERENCES")]
        [NotNull]
        public Preferences? Preferences { get; set; }

    }
}
