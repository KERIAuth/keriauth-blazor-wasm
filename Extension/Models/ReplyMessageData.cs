using System.Text.Json.Serialization;

namespace Extension.Models
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
            Type = type;
            PayloadTypeName = typeof(T).Name;
            RequestId = requestId;
            Payload = payload;
            Error = error;
            Source = source;
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
