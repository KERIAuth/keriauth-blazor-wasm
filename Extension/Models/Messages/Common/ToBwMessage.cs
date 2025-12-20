using System.Text.Json.Serialization;

namespace Extension.Models.Messages.Common {
    /// <summary>
    /// Base message received by BackgroundWorker (inbound direction).
    /// Can come from ContentScript or App.
    /// Non-generic version for initial deserialization when payload type is unknown.
    /// Use to inspect Type property, then deserialize to specific typed message if needed.
    /// </summary>
    public record ToBwMessage {
        [JsonPropertyName("type")]
        public string Type { get; init; }

        [JsonPropertyName("requestId")]
        public string? RequestId { get; init; }

        [JsonPropertyName("payload")]
        public object? Payload { get; init; }

        [JsonConstructor]
        public ToBwMessage(string type, string? requestId = null, object? payload = null) {
            Type = type;
            RequestId = requestId;
            Payload = payload;
        }
    }

    /// <summary>
    /// Generic version of ToBwMessage with typed payload.
    /// Use when the payload type is known at compile time.
    /// </summary>
    public record ToBwMessage<T> {
        [JsonPropertyName("type")]
        public string Type { get; init; }

        [JsonPropertyName("requestId")]
        public string? RequestId { get; init; }

        [JsonPropertyName("payload")]
        public T? Payload { get; init; }

        [JsonConstructor]
        public ToBwMessage(string type, string? requestId = null, T? payload = default) {
            Type = type;
            RequestId = requestId;
            Payload = payload;
        }
    }
}
