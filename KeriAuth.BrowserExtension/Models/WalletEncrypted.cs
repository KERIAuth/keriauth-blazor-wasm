namespace KeriAuth.BrowserExtension.Models
{
    using System.Text.Json.Serialization;

    public class WalletEncrypted
    {
        [JsonConstructor]
        public WalletEncrypted(string content)
        {
            Content = content;
            TimeEncryptedUtc = DateTime.UtcNow;
        }

        [JsonPropertyName("teUTC")]
        public DateTime TimeEncryptedUtc { get; }

        [JsonPropertyName("c")]
        public string Content { get; }
    }
}