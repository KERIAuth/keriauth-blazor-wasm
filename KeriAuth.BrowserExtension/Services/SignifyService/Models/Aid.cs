using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Services.SignifyService.Models
{
    public class Aid
    {
        [JsonConstructor]
        public Aid(string name, string prefix, Salty salty)
        {
            Name = name;
            Prefix = prefix;
            Salty = salty;
        }
        [JsonPropertyName("name")]
        public string Name { get; init; }
        [JsonPropertyName("prefix")]
        public string Prefix { get; init; }
        [JsonPropertyName("salty")]
        public Salty Salty { get; init; }
    }
}
