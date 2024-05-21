namespace KeriAuth.BrowserExtension.Models
{
    using System.Text.Json.Serialization;

    public class WalletLogin
    {
        [JsonConstructor]
        public WalletLogin(string encryptedLogin)
        {
            EncryptedLogin = encryptedLogin;
        }

        [JsonPropertyName("eL")]
        public string EncryptedLogin { get; }
    }
}