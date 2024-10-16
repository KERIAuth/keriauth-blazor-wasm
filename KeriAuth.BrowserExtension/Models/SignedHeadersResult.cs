using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public record SignedHeadersResult
    {
        [JsonConstructor]
        public SignedHeadersResult(
            string signature,
            string signatureInput,
            string signifyResource,
            string signifyTimestamp
        )
        {
            this.Signature = signature;
            this.SignatureInput = signatureInput;
            this.SignifyResource = signifyResource;
            this.SignifyTimestamp = signifyTimestamp;
        }

        [JsonPropertyName("signature")]
        public string Signature { get; }

        [JsonPropertyName("signature-input")]
        public string SignatureInput { get; }

        [JsonPropertyName("signify-resource")]
        public string SignifyResource { get; }

        [JsonPropertyName("signify-timestamp")]
        public string SignifyTimestamp { get; }
    }
}
