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
            this.Type = type;
            this.PayloadTypeName = typeof(T).Name;
            this.RequestId = requestId;
            this.Payload = payload;
            this.Error = error;
            this.Source = source;
        }

        [JsonPropertyName("type")]
        public string Type { get; }

        [JsonPropertyName("payloadTypeName")]
        public string PayloadTypeName { get; }

        [JsonPropertyName("requestId")]
        public string? RequestId { get; }

        [JsonPropertyName("payload")]
        public T Payload { get; }

        [JsonPropertyName("error")]
        public string? Error { get; }

        [JsonPropertyName("source")]
        public string? Source { get; }
    }
}
