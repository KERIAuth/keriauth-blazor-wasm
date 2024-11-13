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

        [JsonPropertyName("adminUrl")]
        public string AdminUrl { get; init; }

        // TODO P2 Adjust design so service-worker gets the passcode from session storage versus it being passed
        [JsonPropertyName("passcode")]
        public string Passcode { get; init; }

        public ApprovedSignRequest(string passcode, string adminUrl, string origin, string requestUrl, string requestMethod, Dictionary<string, string>? initHeaders, string selectedName)
        {
            this.Passcode = passcode;
            this.AdminUrl = adminUrl;
            Origin = origin;
            RequestUrl = requestUrl;
            RequestMethod = requestMethod;
            this.InitHeaders = initHeaders;
            SelectedName = selectedName;
        }
    }
}