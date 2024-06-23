namespace KeriAuth.BrowserExtension.Models;

using System.Text.Json.Serialization;

public class CachedAid
{
    [JsonConstructor]
    public CachedAid(string prefix, string alias, Guid keriaConnectionId)
    {
        Prefix = prefix;
        Alias = alias;
        Identicon = Helper.Identicon.MakeIdenticon(prefix);
        KeriaConnectionId = keriaConnectionId;
    }

    [JsonPropertyName("prefix")]
    public string Prefix { get; init; }

    [JsonPropertyName("alias")]
    public string Alias { get; init; }

    [JsonPropertyName("identicon")]
    public string Identicon { get; init; }

    [JsonPropertyName("keriaclientidentifier")]
    public Guid KeriaConnectionId { get; init; }

    [JsonPropertyName("cachedUtc")]
    public DateTime CachedUtc { get; init; } = DateTime.UtcNow;
}
