using System.Text.Json.Serialization;

namespace Extension.Models {
    public record AuthorizeResultCredential : IEquatable<AuthorizeResultCredential> {
        [JsonConstructor]
        public AuthorizeResultCredential(string rawJson, string cesr) {
            RawJson = rawJson;
            Cesr = cesr;
        }

        [JsonPropertyName("rawJson")]
        public string RawJson { get; }


        [JsonPropertyName("cesr")]
        public string Cesr { get; }
    }
}
