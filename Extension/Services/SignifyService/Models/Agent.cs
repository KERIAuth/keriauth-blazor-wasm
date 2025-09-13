using System.Text.Json.Serialization;

namespace Extension.Services.SignifyService.Models
{
    public class Agent
    {
        [JsonPropertyName("vn")]
        public List<int> Vn { get; set; } = [];
        
        [JsonPropertyName("i")]
        public string I { get; set; } = string.Empty;
        
        [JsonPropertyName("s")]
        public string S { get; set; } = string.Empty;
        
        [JsonPropertyName("p")]
        public string P { get; set; } = string.Empty;
        
        [JsonPropertyName("d")]
        public string D { get; set; } = string.Empty;
        
        [JsonPropertyName("f")]
        public string F { get; set; } = string.Empty;
        
        [JsonPropertyName("dt")]
        public string Dt { get; set; } = string.Empty;
        
        [JsonPropertyName("et")]
        public string Et { get; set; } = string.Empty;
        
        [JsonPropertyName("kt")]
        public string Kt { get; set; } = string.Empty;
        
        [JsonPropertyName("k")]
        public List<string> K { get; set; } = [];
        
        [JsonPropertyName("nt")]
        public string Nt { get; set; } = string.Empty;
        
        [JsonPropertyName("n")]
        public List<string> N { get; set; } = [];
        
        [JsonPropertyName("bt")]
        public string Bt { get; set; } = string.Empty;
        
        [JsonPropertyName("b")]
        public List<string> B { get; set; } = [];
        
        [JsonPropertyName("c")]
        public List<string> C { get; set; } = [];
        
        [JsonPropertyName("ee")]
        public StateEe Ee { get; set; } = new();
        
        [JsonPropertyName("di")]
        public string Di { get; set; } = string.Empty;
    }
}
