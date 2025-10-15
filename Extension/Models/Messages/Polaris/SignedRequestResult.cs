using System.Text.Json.Serialization;
namespace Extension.Models.Messages.Polaris {
    public record SignedRequestResult {
        [JsonConstructor]
        public SignedRequestResult(
            SignedHeadersResult headers
        ) {
            Headers = headers;
        }

        [JsonPropertyName("headers")]
        public SignedHeadersResult Headers { get; }
    }
}
