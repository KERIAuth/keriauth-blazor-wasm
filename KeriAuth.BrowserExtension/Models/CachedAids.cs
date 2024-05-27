using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public class CachedAids
    {
        [JsonConstructor]
        public CachedAids(List<CachedAid> aids)
        {
            Aids = aids;
        }

        [JsonPropertyName("cachedAids")]
        public List<CachedAid> Aids { get; init; }
    }
}
