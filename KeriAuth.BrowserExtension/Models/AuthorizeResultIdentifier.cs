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
            this.prefix = prefix;
        }

        [JsonPropertyName("prefix")]
        public string prefix { get; }
    }
}
