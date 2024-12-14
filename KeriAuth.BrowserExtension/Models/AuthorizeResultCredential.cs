using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public record AuthorizeResultCredential : IEquatable<AuthorizeResultCredential>
    {
        [JsonConstructor]
        public AuthorizeResultCredential(string rawJson, string cesr)
        {
            this.RawJson = rawJson;
            this.Cesr = cesr;
        }

        [JsonPropertyName("rawJson")]
        public string RawJson { get; }


        [JsonPropertyName("cesr")]
        public string Cesr { get; }
    }
}
