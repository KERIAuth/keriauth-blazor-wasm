using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public record AuthorizeResultIdentifier : IEquatable<AuthorizeResultIdentifier>
    {
        [JsonConstructor]
        public AuthorizeResultIdentifier(
            string prefix,
            string alias
            )
        {
            this.Prefix = prefix;
            this.Alias = alias;
        }

        [JsonPropertyName("prefix")]
        public string Prefix { get; }
        
        [JsonPropertyName("alias")]
        public string Alias { get; }
    }
}
