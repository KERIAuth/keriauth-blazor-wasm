using System.Text.Json.Serialization;

namespace Extension.Services.SignifyService.Models {
    public record Agent {
        [JsonPropertyName("vn")]
        public List<int> Vn { get; init; } = [];

        [JsonPropertyName("i")]
        public string I { get; init; } = string.Empty;

        [JsonPropertyName("s")]
        public string S { get; init; } = string.Empty;

        [JsonPropertyName("p")]
        public string P { get; init; } = string.Empty;

        [JsonPropertyName("d")]
        public string D { get; init; } = string.Empty;

        [JsonPropertyName("f")]
        public string F { get; init; } = string.Empty;

        [JsonPropertyName("dt")]
        public string Dt { get; init; } = string.Empty;

        [JsonPropertyName("et")]
        public string Et { get; init; } = string.Empty;

        [JsonPropertyName("kt")]
        public string Kt { get; init; } = string.Empty;

        [JsonPropertyName("k")]
        public List<string> K { get; init; } = [];

        [JsonPropertyName("nt")]
        public string Nt { get; init; } = string.Empty;

        [JsonPropertyName("n")]
        public List<string> N { get; init; } = [];

        [JsonPropertyName("bt")]
        public string Bt { get; init; } = string.Empty;

        [JsonPropertyName("b")]
        public List<string> B { get; init; } = [];

        [JsonPropertyName("c")]
        public List<string> C { get; init; } = [];

        [JsonPropertyName("ee")]
        public StateEe Ee { get; init; } = new();

        [JsonPropertyName("di")]
        public string Di { get; init; } = string.Empty;
    }
}
