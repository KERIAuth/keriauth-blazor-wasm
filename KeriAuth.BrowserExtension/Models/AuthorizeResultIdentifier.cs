using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public record AuthorizeResultIdentifier : IEquatable<AuthorizeResultIdentifier>
    {
        [JsonConstructor]
        public AuthorizeResultIdentifier(
            string prefix
            )
        {
            this.Prefix = prefix;
        }

        [JsonPropertyName("prefix")]
        public string Prefix { get; }
    }
}
