using Extension.Models.Messages.Common;
using System.Text.Json.Serialization;

namespace Extension.Models.Messages.Polaris {
    // See also ISignin in signify-browser-extension https://github.com/WebOfTrust/signify-browser-extension/blob/main/src/config/types.ts#L46
    public record AuthorizeResult {
        [JsonPropertyName("credential")]
        public required AuthorizeResultCredential? ARCredential { get; init; }

        [JsonPropertyName("identifier")]
        public required AuthorizeResultIdentifier? ARIdentifier { get; init; }

        [JsonPropertyName("expiry")]
        public long Expiry { get; init; } = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
    }
}
