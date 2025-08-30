using System.Text.Json.Serialization;
namespace Extension.Models
{
    public record SignedRequestResult
    {
        [JsonConstructor]
        public SignedRequestResult(
            SignedHeadersResult headers
        )
        {
            Headers = headers;
        }

        [JsonPropertyName("headers")]
        public SignedHeadersResult Headers { get; }
    }
}
