namespace KeriAuth.BrowserExtension.Models
{
    using System.Text.Json.Serialization;

    [method: JsonConstructor]
    public class WalletLogin(string encryptedLogin)
    {
        [JsonPropertyName("eL")]
        public string EncryptedLogin { get; } = encryptedLogin;
    }
}