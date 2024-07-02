using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Services.SignifyService.Models
{
    public record Credential
    {
        //[JsonPropertyName("anc")]
        //public string? Anc { get; init; }
        
        //[JsonPropertyName("iss")]
        //public Dictionary<string, string>? Iss { get; init; }
        
        [JsonPropertyName("pre")]
        public string? Pre { get; init; }
    }
}
