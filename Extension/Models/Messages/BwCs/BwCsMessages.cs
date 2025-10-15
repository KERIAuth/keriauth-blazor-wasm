using System.Text.Json.Serialization;

namespace Extension.Models.Messages.ExCs {
    /// <summary>
    /// Message sent from BackgroundWorker to ContentScript (outbound direction).
    /// </summary>
    public record BwCsMessage {
        [JsonPropertyName("type")]
        public string Type { get; init; }

        [JsonPropertyName("requestId")]
        public string? RequestId { get; init; }

        [JsonPropertyName("payload")]
        public object? Payload { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }

        [JsonConstructor]
        public BwCsMessage(string type, string? requestId = null, object? payload = null, string? error = null) {
            Type = type;
            RequestId = requestId;
            Payload = payload;
            Error = error;
        }
    }

    /// <summary>
    /// Message types sent from BackgroundWorker to ContentScript.
    /// </summary>
    public static class BwCsMessageTypes {
        public const string READY = "ready";
        public const string REPLY = "/signify/reply";
        public const string REPLY_CANCELED = "reply_canceled";
        public const string REPLY_CREDENTIAL = "/KeriAuth/signify/replyCredential";
        public const string FROM_BACKGROUND_WORKER = "fromBackgroundWorker";
        public const string APP_CLOSED = "app_closed";
    }

    /// <summary>
    /// Specialized READY message sent in response to ContentScript INIT.
    /// </summary>
    public record ReadyMessage : BwCsMessage {
        public ReadyMessage() : base(BwCsMessageTypes.READY) { }
    }

    /// <summary>
    /// Reply message with payload of type T.
    /// </summary>
    public record ReplyMessage<T> : BwCsMessage {
        public ReplyMessage(string? requestId, T? payload)
            : base(BwCsMessageTypes.REPLY, requestId, payload) { }
    }

    /// <summary>
    /// Error reply message.
    /// </summary>
    public record ErrorReplyMessage : BwCsMessage {
        public ErrorReplyMessage(string? requestId, string error)
            : base(BwCsMessageTypes.REPLY, requestId, null, error) { }
    }
}
