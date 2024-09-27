using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public record AuthorizeResultCredential : IEquatable<AuthorizeResultCredential>
    {
        [JsonConstructor]
        public AuthorizeResultCredential(string rawJson, string cesr)
        {
            this.rawJson = rawJson;
            this.cesr = cesr;
        }

        [JsonPropertyName("rawJson")]
        public string rawJson { get; }


        [JsonPropertyName("cesr")]
        public string cesr { get; }
    }
}
