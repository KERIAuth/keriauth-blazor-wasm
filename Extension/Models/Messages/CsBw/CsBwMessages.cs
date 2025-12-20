using System.Text.Json.Serialization;
using Extension.Models.Messages.Common;

namespace Extension.Models.Messages.CsBw {
    /// <summary>
    /// Message from ContentScript to BackgroundWorker.
    /// Originates from web page via polaris-web protocol.
    /// Non-generic version for convenience when constructing messages with untyped payload.
    /// For initial deserialization when type is unknown, use ToBwMessage directly.
    /// </summary>
    public record CsBwMessage : ToBwMessage<object> {
        public CsBwMessage(string type, string? requestId = null, object? payload = null)
            : base(type, requestId, payload) { }
    }

    /// <summary>
    /// Message from ContentScript to BackgroundWorker with typed payload.
    /// Originates from web page via polaris-web protocol.
    /// </summary>
    public record CsBwMessage<T> : ToBwMessage<T> {
        public CsBwMessage(string type, string? requestId = null, T? payload = default)
            : base(type, requestId, payload) { }
    }

    /*
    /// <summary>
    /// Message from App (popup/tab/sidepanel) to BackgroundWorker.
    /// Used for replies that need to be forwarded to ContentScript.
    /// </summary>
    public record AppBwMessage : ToBwMessage {
        /// <summary>
        /// Tab ID where the message should be forwarded (for replies to ContentScript).
        /// </summary>
        [JsonPropertyName("tabId")]
        public int? TabId { get; init; }

        [JsonConstructor]
        public AppBwMessage(string type, int? tabId = null, string? requestId = null, object? payload = null)
            : base(type, requestId, payload) {
            TabId = tabId;
        }
    }
    */

    /// <summary>
    /// Payload for CREATE_DATA_ATTESTATION message.
    /// Contains credential data and schema SAID for data attestation credential creation.
    /// </summary>
    public record CreateDataAttestationPayload(
        [property: JsonPropertyName("credData")] Dictionary<string, object> CredData,
        [property: JsonPropertyName("schemaSaid")] string SchemaSaid
    );

    /// <summary>
    /// Generic internal extension runtime message structure for chrome.runtime messages.
    /// Used for simple internal communication between extension components.
    /// </summary>
    public record RuntimeMessage(
        [property: JsonPropertyName("action")] string? Action = null,
        [property: JsonPropertyName("type")] string? Type = null,
        [property: JsonPropertyName("data")] object? Data = null,
        [property: JsonPropertyName("payload")] object? Payload = null
    );

    /// <summary>
    /// Message types originating from ContentScript (via web page polaris-web protocol).
    /// These are received by BackgroundWorker from ContentScript.
    /// </summary>
    public static class CsBwMessageTypes {
        // ContentScript initialization
        public const string INIT = "init";

        // Polaris-web protocol messages from web page
        public const string SIGNIFY_EXTENSION = "signify-extension";
        public const string SIGNIFY_EXTENSION_CLIENT = "signify-extension-client";
        public const string CONFIGURE_VENDOR = "/signify/configure-vendor";
        public const string AUTHORIZE = "/signify/authorize";
        public const string SELECT_AUTHORIZE_AID = "/signify/authorize/aid";
        public const string SELECT_AUTHORIZE_CREDENTIAL = "/signify/authorize/credential";
        public const string SIGN_DATA = "/signify/sign-data";
        public const string SIGN_REQUEST = "/signify/sign-request";
        public const string GET_SESSION_INFO = "/signify/get-session-info";
        public const string CLEAR_SESSION = "/signify/clear-session";
        public const string CREATE_DATA_ATTESTATION = "/signify/credential/create/data-attestation";
        public const string GET_CREDENTIAL = "/signify/credential/get";
    }

    /*
    /// <summary>
    /// Message types originating from App (popup/tab/sidepanel).
    /// These are received by BackgroundWorker and typically forwarded to ContentScript.
    /// </summary>
    public static class AppBwMessageTypes {
        public const string REPLY_CREDENTIAL = "/KeriAuth/signify/replyCredential";
        public const string REPLY_CANCELED = "/KeriAuth/signify/reply_canceled";
        public const string REPLY_SIGN = "/KeriAuth/signify/replySign";
        public const string REPLY_ERROR = "/KeriAuth/signify/replyError";
        public const string REPLY_IDENTIFIER = "/KeriAuth/signify/replyIdentifier";
        public const string REPLY_AID = "/KeriAuth/signify/replyAID";
        public const string APP_CLOSED = "/KeriAuth/signify/app_closed";
        public const string REPLY_APPROVED_SIGN_HEADERS = "/KeriAuth/signify/reply_approved_sign_headers";
    }
    */
}
