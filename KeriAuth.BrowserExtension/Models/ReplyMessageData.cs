using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public record ReplyMessageData<T>
    {
        // See how this relates to the `RequestMessageData` class

        [JsonConstructor]
        public ReplyMessageData(
            string type,
            T payload,
            string? requestId = null,
            string? error = null,
            string? source = null)
        {
            this.type = type;
            this.payloadTypeName = typeof(T).Name;
            this.requestId = requestId;
            this.payload = payload;
            this.error = error;
            this.source = source;
        }

        [JsonPropertyName("type")]
        public string type { get; }

        [JsonPropertyName("payloadTypeName")]
        public string payloadTypeName { get; }

        [JsonPropertyName("requestId")]
        public string? requestId { get; }

        [JsonPropertyName("payload")]
        public T payload { get; }

        [JsonPropertyName("error")]
        public string? error { get; }

        [JsonPropertyName("source")]
        public string? source { get; }
    }
}
