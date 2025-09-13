using System.Text.Json.Serialization;

namespace Extension.Services.SignifyService.Models {
    [method: JsonConstructor]
    public class Identifiers(int start, int end, int total, List<Aid> aids) {
        [JsonPropertyName("start")]
        public int Start { get; init; } = start;
        [JsonPropertyName("end")]
        public int End { get; init; } = end;
        [JsonPropertyName("total")]
        public int Total { get; init; } = total;
        [JsonPropertyName("aids")]
        public List<Aid> Aids { get; init; } = aids;
    }
}
