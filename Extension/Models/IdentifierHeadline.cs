using System.Text.Json.Serialization;

namespace Extension.Models
{
    public class IdentifierHeadline
    {
        [JsonConstructor]
        public IdentifierHeadline(string prefix, string alias, Guid keriaConfigId)
        {
            Alias = alias;
            Prefix = prefix;
            Identicon = Helper.Identicon.MakeIdenticon(prefix);
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
