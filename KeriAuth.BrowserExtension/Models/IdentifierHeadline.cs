using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public class IdentifierHeadline
    {
        [JsonConstructor]
        public IdentifierHeadline(string prefix, string alias, Guid keriaConfigId)
        {
            this.Alias = alias;
            this.Prefix = prefix;
            this.Identicon = Helper.Identicon.MakeIdenticon(prefix);
            _ = keriaConfigId;
        }

        [JsonPropertyName("prefix")]
        public string Prefix { get; init; } = "";

        [JsonPropertyName("alias")]
        public string Alias { get; init; } = "";

        [JsonPropertyName("identicon")]
        public string Identicon { get; init; } = "";

    }
}