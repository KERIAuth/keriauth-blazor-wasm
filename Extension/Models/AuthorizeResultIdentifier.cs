using System.Text.Json.Serialization;

namespace Extension.Models {
    public record AuthorizeResultIdentifier : IEquatable<AuthorizeResultIdentifier> {
        [JsonConstructor]
        public AuthorizeResultIdentifier(
            string prefix,
            string alias
            ) {
            Prefix = prefix;
            Alias = alias;
        }

        [JsonPropertyName("prefix")]
        public string Prefix { get; }

        [JsonPropertyName("alias")]
        public string Alias { get; }
    }
}
