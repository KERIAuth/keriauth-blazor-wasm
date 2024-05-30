namespace KeriAuth.BrowserExtension.Models;

using System.Text.Json.Serialization;

public class CachedAid
{
    // implement this class with the JsonProperty attributes and JsonConstructor attribute for: Prefix, AID, Alias, and Identicon

    [JsonConstructor]
    public CachedAid(string prefix, string alias, string keriaclientidentifier)
    {
        // set the properties
        Prefix = prefix;
        Alias = alias;
        Identicon = Helper.Identicon.MakeIdenticon(prefix);
        KeriaClientIdentifier = keriaclientidentifier;
    }

    [JsonPropertyName("prefix")]
    public string Prefix { get; init; }

    [JsonPropertyName("alias")]
    public string Alias { get; init; }

    [JsonPropertyName("identicon")]
    public string Identicon { get; init; }

    [JsonPropertyName("keriaclientidentifier")]
    public string KeriaClientIdentifier { get; init; }

    [JsonPropertyName("cachedUtc")]
    public DateTime CachedUtc { get; init; } = DateTime.UtcNow;
}
