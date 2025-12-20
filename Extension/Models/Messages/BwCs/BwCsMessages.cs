using System.Text.Json.Serialization;
using Extension.Models.Messages.Common;

namespace Extension.Models.Messages.ExCs {
    /// <summary>
    /// Message sent from BackgroundWorker to ContentScript (outbound direction).
    /// Non-generic version for initial deserialization.
    /// </summary>
    public record BwCsMessage : FromBwMessage {
        public BwCsMessage(string type, string? requestId = null, object? data = null, string? error = null)
            : base(type, requestId, data, error) { }
    }

    /// <summary>
    /// Message sent from BackgroundWorker to ContentScript with typed payload.
    /// </summary>
    public record BwCsMessage<T> : FromBwMessage<T> {
        public BwCsMessage(string type, string? requestId = null, T? data = default, string? error = null)
            : base(type, requestId, data, error) { }
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
    /// Reply message with data of type T.
    /// </summary>
    public record ReplyMessage<T> : BwCsMessage<T> {
        public ReplyMessage(string? requestId, T? data)
            : base(BwCsMessageTypes.REPLY, requestId, data) { }
    }

    /// <summary>
    /// Error reply message.
    /// </summary>
    public record ErrorReplyMessage : BwCsMessage {
        public ErrorReplyMessage(string? requestId, string error)
            : base(BwCsMessageTypes.REPLY, requestId, null, error) { }
    }

    /// <summary>
    /// Identifier payload conforming to polaris-web AuthorizeResultIdentifier.
    /// Contains prefix (required) and optionally name as expected by the protocol.
    /// </summary>
    public record BwCsAuthorizeResultIdentifier(
        [property: JsonPropertyName("prefix")] string Prefix,
        [property: JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null
    );

    /// <summary>
    /// Credential payload conforming to polaris-web AuthorizeResultCredential.
    /// </summary>
    public record BwCsAuthorizeResultCredential(
        [property: JsonPropertyName("raw")] object Raw,
        [property: JsonPropertyName("cesr")] string Cesr
    );

    /// <summary>
    /// Payload conforming to polaris-web AuthorizeResult interface.
    /// Used when sending authorization replies to ContentScript → Page.
    ///
    /// Expected by polaris-web:
    /// {
    ///     identifier?: { prefix: string },
    ///     credential?: { raw: unknown, cesr: string },
    ///     headers?: Record&lt;string, string&gt;
    /// }
    /// </summary>
    public record BwCsAuthorizeResultPayload(
        [property: JsonPropertyName("identifier"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BwCsAuthorizeResultIdentifier? Identifier = null,
        [property: JsonPropertyName("credential"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BwCsAuthorizeResultCredential? Credential = null,
        [property: JsonPropertyName("headers"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Dictionary<string, string>? Headers = null
    );
}
