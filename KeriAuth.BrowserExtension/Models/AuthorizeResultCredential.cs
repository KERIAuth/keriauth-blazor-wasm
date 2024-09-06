using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public record AuthorizeResultCredential : IEquatable<AuthorizeResultCredential>
    {
        [JsonConstructor]
        public AuthorizeResultCredential(string raw, string cesr)
        {
            this.raw = raw;
            this.cesr = cesr;
        }

        [JsonPropertyName("raw")]
        public string raw { get; }


        [JsonPropertyName("cesr")]
        public string cesr { get; }
    }
}
