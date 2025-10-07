using System.Text.Json.Serialization;
using Extension.Helper;

namespace Extension.Models {
    public record AuthorizeResultCredential : IEquatable<AuthorizeResultCredential> {
        [JsonConstructor]
        public AuthorizeResultCredential(RecursiveDictionary raw, string cesr) {
            Raw = raw;
            Cesr = cesr;
        }

        // Credential stored as RecursiveDictionary to preserve CESR/SAID ordering
        // NEVER serialize/deserialize this - RecursiveDictionary maintains insertion order
        [JsonPropertyName("raw")]
        [JsonConverter(typeof(RecursiveDictionaryConverter))]
        public RecursiveDictionary Raw { get; }

        [JsonPropertyName("cesr")]
        public string Cesr { get; }
    }
}
