using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public record ApprovedSignRequest
    {
        [JsonPropertyName("origin")]
        public string Origin { get; init; }

        [JsonPropertyName("requestUrl")]
        public string RequestUrl { get; init; }

        [JsonPropertyName("requestMethod")]
        public string RequestMethod { get; init; }

        [JsonPropertyName("headers")]
        public Dictionary<string, string>? InitHeaders { get; init; }

        [JsonPropertyName("selectedName")]
        public string SelectedName { get; init; }

        public ApprovedSignRequest(string origin, string requestUrl, string requestMethod, Dictionary<string, string>? initHeaders, string selectedName)
        {
            Origin = origin;
            RequestUrl = requestUrl;
            RequestMethod = requestMethod;
            this.InitHeaders = initHeaders;
            SelectedName = selectedName;
        }
    }
}