using System.Text.Json.Serialization;
using Extension.Helper;
using Extension.Services.SignifyService.Models;

namespace Extension.Models.Messages.AppBw {
    /// <summary>
    /// Base record for all messages sent from App (popup/tab/sidepanel) to BackgroundWorker.
    /// Direction: App → BackgroundWorker
    /// Not abstract, because it is helpful as first step of instantiating more specific type
    /// </summary>
    public record AppBwMessage<T> {
        [JsonPropertyName("type")]
        public string Type { get; init; }

        [JsonPropertyName("tabId")]
        public int TabId { get; init; }

        [JsonPropertyName("tabUrl")]
        public string? TabUrl { get; init; }

        [JsonPropertyName("requestId")]
        public string? RequestId { get; init; }

        [JsonPropertyName("payload")]
        public T? Payload { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }

        [JsonConstructor]
        public AppBwMessage(string type, int tabId, string? tabUrl, string? requestId = null, T? payload = default, string? error = null) {
            Type = type;
            TabId = tabId;
            TabUrl = tabUrl;
            RequestId = requestId;
            Payload = payload;
            Error = error;
        }
    }

    /// <summary>
    /// Message types sent from App to BackgroundWorker.
    /// </summary>
    public static class AppBwMessageTypes {
        public const string REPLY_CREDENTIAL = "/KeriAuth/signify/replyCredential";
        public const string REPLY_CANCELED = "/KeriAuth/signify/replyCanceled";
        public const string REPLY_ERROR = "/KeriAuth/signify/replyError";
        public const string REPLY_IDENTIFIER = "/KeriAuth/signify/replyIdentifier";
        public const string REPLY_AID = "/KeriAuth/signify/replyAID";
        public const string REPLY_APPROVED_SIGN_HEADERS = "/KeriAuth/signify/approvedSignHeaders";
        public const string APP_CLOSED = "/KeriAuth/signify/app_closed";
    }

    /// <summary>
    /// Reply message containing a credential.
    /// Used when user selects and authorizes a credential to be shared with a web page.
    /// </summary>
    public record AppBwReplyCredentialMessage : AppBwMessage<RecursiveDictionary> {
        public AppBwReplyCredentialMessage(int tabId, string? tabUrl, string requestId, RecursiveDictionary credential)
            : base(AppBwMessageTypes.REPLY_CREDENTIAL, tabId, tabUrl, requestId, credential) { }
    }

    /// <summary>
    /// Reply message containing an AID (Autonomic Identifier) authorization result.
    /// Used when user selects and authorizes an identifier for a web page.
    /// </summary>
    public record AppBwReplyAidMessage : AppBwMessage<AuthorizeResult> {
        public AppBwReplyAidMessage(int tabId, string? tabUrl, string requestId, AuthorizeResult authorizeResult)
            : base(AppBwMessageTypes.REPLY_AID, tabId, tabUrl, requestId, authorizeResult) { }
    }

    /// <summary>
    /// Payload for sign approval reply message.
    /// </summary>
    public record AppBwReplySignPayload1(
        [property: JsonPropertyName("headersDict")] Dictionary<string, string> HeadersDict,
        [property: JsonPropertyName("prefix")] string Prefix,
        [property: JsonPropertyName("isApproved")] bool IsApproved
    );

    /// <summary>
    /// Payload for approved sign request.
    /// Contains all information needed to sign an HTTP request.
    /// </summary>
    public record AppBwReplySignPayload2(
        [property: JsonPropertyName("origin")] string Origin,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("method")] string Method,
        [property: JsonPropertyName("headers")] Dictionary<string, string> Headers,
        [property: JsonPropertyName("prefix")] string Prefix
    );

    /// <summary>
    /// Reply message from App containing user's sign approval or reject.
    /// </summary>
    public record AppBwReplySignMessage : AppBwMessage<AppBwReplySignPayload1> {
        public AppBwReplySignMessage(int tabId, string? tabUrl, string requestId, Dictionary<string, string> headersDict, string prefix, bool isApproved)
            : base(AppBwMessageTypes.REPLY_APPROVED_SIGN_HEADERS, tabId, tabUrl, requestId, payload: new AppBwReplySignPayload1(headersDict, prefix, isApproved)) { }
    }

    /// <summary>
    /// Reply message indicating an error occurred.
    /// </summary>
    public record AppBwReplyErrorMessage : AppBwMessage<object> {
        public AppBwReplyErrorMessage(int tabId, string? tabUrl, string requestId, string errorMessage)
            : base(AppBwMessageTypes.REPLY_ERROR, tabId, tabUrl, requestId, new object(), errorMessage) { }
    }

    /// <summary>
    /// Reply message indicating the user canceled the operation.
    /// </summary>
    public record AppBwReplyCanceledMessage : AppBwMessage<object> {
        public AppBwReplyCanceledMessage(int tabId, string? tabUrl, string requestId, string? errorMessage = null)
            : base(AppBwMessageTypes.REPLY_CANCELED, tabId, tabUrl, requestId, new object(), errorMessage ?? "User cancelled") { }
    }

    /// <summary>
    /// Message indicating the App (popup/tab) was closed by the user.
    /// </summary>
    public record AppBwAppClosedMessage : AppBwMessage<object> {
        public AppBwAppClosedMessage(int tabId, string? tabUrl, string requestId)
            : base(AppBwMessageTypes.APP_CLOSED,  tabId, tabUrl, requestId) { }
    }

    /// <summary>
    /// Result of authorization containing identifier and optional credential information.
    /// </summary>
    public record AuthorizeResult(
        [property: JsonPropertyName("aid")] Aid Aid,
        [property: JsonPropertyName("credential")] AuthorizeResultCredential? Credential = null
    );

    /// <summary>
    /// Credential information containing both the raw credential object and its CESR string representation.
    /// </summary>
    public record AuthorizeResultCredential(
        [property: JsonPropertyName("raw")] RecursiveDictionary Raw,
        [property: JsonPropertyName("cesr")] string Cesr
    );
}
