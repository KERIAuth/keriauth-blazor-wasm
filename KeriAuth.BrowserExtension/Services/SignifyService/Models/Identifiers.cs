using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Services.SignifyService.Models
{
    public class Identifiers
    {
        [JsonConstructor]
        public Identifiers(int start, int end, int total, List<Aid> aids)
        {
            Start = start;
            End = end;
            Total = total;
            Aids = aids;
        }
        [JsonPropertyName("start")]
        public int Start { get; init; }
        [JsonPropertyName("end")]
        public int End { get; init; }
        [JsonPropertyName("total")]
        public int Total { get; init; }
        [JsonPropertyName("aids")]
        public List<Aid> Aids { get; init; }
    }
}
