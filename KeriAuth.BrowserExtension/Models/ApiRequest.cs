using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public record ApiRequest
    {
        [JsonPropertyName("url")]
        public string Url { get; init; } = string.Empty;

        [JsonPropertyName("method")]
        public string Method { get; init; } = "GET";

        // Default constructor
        public ApiRequest() { }

        // Constructor with parameters (if needed)
        public ApiRequest(string url, string method)
        {
            Url = url;
            Method = method;
        }
    }
}
