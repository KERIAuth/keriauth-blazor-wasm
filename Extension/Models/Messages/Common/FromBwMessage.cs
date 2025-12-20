using System.Text.Json;
using System.Text.Json.Serialization;

namespace Extension.Models.Messages.Common {
    /// <summary>
    /// Base message sent from BackgroundWorker (outbound direction).
    /// Can be sent to App or ContentScript.
    /// Non-generic version for initial deserialization when payload type is unknown.
    /// Use to inspect Type property, then deserialize to specific typed message if needed.
    /// Note: Uses "data" as JSON property name for payload to match polaris-web protocol.
    /// Data is JsonElement? to make the JSON structure explicit (rather than hiding it behind object).
    /// </summary>
    public record FromBwMessage {
        [JsonPropertyName("type")]
        public string Type { get; init; }

        [JsonPropertyName("requestId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RequestId { get; init; }

        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public JsonElement? Data { get; init; }

        [JsonPropertyName("error")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Error { get; init; }

        [JsonConstructor]
        public FromBwMessage(string type, string? requestId = null, JsonElement? data = null, string? error = null) {
            Type = type;
            RequestId = requestId;
            Data = data;
            Error = error;
        }
    }

    /// <summary>
    /// Generic version of FromBwMessage with typed payload.
    /// Use when the payload type is known at compile time.
    /// Note: Uses "data" as JSON property name for payload to match polaris-web protocol.
    /// </summary>
    public record FromBwMessage<T> {
        [JsonPropertyName("type")]
        public string Type { get; init; }

        [JsonPropertyName("requestId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RequestId { get; init; }

        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public T? Data { get; init; }

        [JsonPropertyName("error")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Error { get; init; }

        [JsonConstructor]
        public FromBwMessage(string type, string? requestId = null, T? data = default, string? error = null) {
            Type = type;
            RequestId = requestId;
            Data = data;
            Error = error;
        }
    }
}
