using System.Text.Json.Serialization;

namespace Extension.Models
{
    public record ApprovedSignRequest
    {
        [JsonPropertyName("origin")]
        public string Origin { get; init; }

        [JsonPropertyName("url")]
        public string Url { get; init; }

        [JsonPropertyName("method")]
        public string Method { get; init; }

        [JsonPropertyName("headers")]
        public Dictionary<string, string>? InitHeaders { get; init; }

        [JsonPropertyName("selectedName")]
        public string SelectedName { get; init; }

        [JsonPropertyName("adminUrl")]
        public string AdminUrl { get; init; }

        // TODO P2 Adjust design so BackgroundWorker gets the passcode from session storage versus it being passed
        [JsonPropertyName("passcode")]
        public string Passcode { get; init; }

        public ApprovedSignRequest(string passcode, string adminUrl, string origin, string url, string method, Dictionary<string, string>? initHeaders, string selectedName)
        {
            Passcode = passcode;
            AdminUrl = adminUrl;
            Origin = origin;
            Url = url;
            Method = method;
            InitHeaders = initHeaders;
            SelectedName = selectedName;
        }
    }
}
