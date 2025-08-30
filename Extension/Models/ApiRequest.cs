using System.Text.Json.Serialization;

namespace Extension.Models
{
    public record ApiRequest
    {
        [JsonPropertyName("url")]
        public string Url { get; init; } = string.Empty;

        [JsonPropertyName("method")]
        public string Method { get; init; } = "unset";

        [JsonPropertyName("headersDict")]
        public Dictionary<string, string>? HeadersDict { get; init; }

        // Default constructor
        public ApiRequest() { }

        // Constructor with parameters (if needed)
        public ApiRequest(string url, string method, Dictionary<string, string>? headersDict)
        {
            Url = url;
            Method = method;
            HeadersDict = headersDict;
        }
    }
}
