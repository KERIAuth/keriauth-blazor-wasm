using System.Text.Json.Serialization;

namespace Extension.Models {
    public record AuthorizeResultCredential : IEquatable<AuthorizeResultCredential> {
        [JsonConstructor]
        public AuthorizeResultCredential(object raw, string cesr) {
            Raw = raw;
            Cesr = cesr;
        }

        [JsonPropertyName("raw")]
        public object Raw { get; }


        [JsonPropertyName("cesr")]
        public string Cesr { get; }
    }
}
