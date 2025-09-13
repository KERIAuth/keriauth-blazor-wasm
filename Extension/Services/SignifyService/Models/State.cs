using System.Text.Json.Serialization;

namespace Extension.Services.SignifyService.Models
{
    public record State
    {
        [JsonPropertyName("agent")]
        public Agent? Agent { get; init; }
        
        [JsonPropertyName("controller")]
        public Controller? Controller { get; init; }
        
        [JsonPropertyName("ridx")]
        public int Ridx { get; init; }
        
        [JsonPropertyName("pidx")]
        public int Pidx { get; init; }
    }

    public record StateEe
    {
        [JsonPropertyName("s")]
        public string S { get; init; } = string.Empty;
        
        [JsonPropertyName("d")]
        public string D { get; init; } = string.Empty;

        [JsonPropertyName("br")]
        public List<string> Br { get; init; } = [];
        
        [JsonPropertyName("ba")]
        public List<string> Ba { get; init; } = [];
    }

    public record ControllerEe
    {
        [JsonPropertyName("v")]
        public string V { get; init; } = string.Empty;
        
        [JsonPropertyName("t")]
        public string T { get; init; } = string.Empty;
        
        [JsonPropertyName("d")]
        public string D { get; init; } = string.Empty;
        
        [JsonPropertyName("i")]
        public string I { get; init; } = string.Empty;
        
        [JsonPropertyName("s")]
        public string S { get; init; } = string.Empty;
        
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
        
        [JsonPropertyName("a")]
        public List<string> A { get; init; } = [];
    }

    public record ControllerState
    {
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
