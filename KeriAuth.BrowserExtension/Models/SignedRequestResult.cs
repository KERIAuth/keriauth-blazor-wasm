using System.Text.Json.Serialization;
namespace KeriAuth.BrowserExtension.Models
{
    public record SignedRequestResult
    {
        [JsonConstructor]
        public SignedRequestResult(
            SignedHeadersResult headers
        )
        {
            this.Headers = headers;
        }

        [JsonPropertyName("headers")]
        public SignedHeadersResult Headers { get; }
    }
}
