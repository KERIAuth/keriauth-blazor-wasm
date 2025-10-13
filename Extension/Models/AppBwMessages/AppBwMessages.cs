using System.Text.Json.Serialization;
using Extension.Helper;
using Extension.Services.SignifyService.Models;

namespace Extension.Models.AppBwMessages {
    /// <summary>
    /// Base record for all messages sent from App (popup/tab/sidepanel) to BackgroundWorker.
    /// Direction: App → BackgroundWorker
    /// </summary>
    public abstract record AppBwMessage {
        [JsonPropertyName("type")]
        public string Type { get; init; }

        [JsonPropertyName("tabId")]
        public int? TabId { get; init; }

        [JsonPropertyName("requestId")]
        public string? RequestId { get; init; }

        [JsonPropertyName("payload")]
        public object? Payload { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }

        protected AppBwMessage(string type, int? tabId = null, string? requestId = null, object? payload = null, string? error = null) {
            Type = type;
            TabId = tabId;
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
        public const string REPLY_CANCELED = "/KeriAuth/signify/reply_canceled";
        public const string REPLY_SIGN = "/KeriAuth/signify/replySign";
        public const string REPLY_ERROR = "/KeriAuth/signify/replyError";
        public const string REPLY_IDENTIFIER = "/KeriAuth/signify/replyIdentifier";
        public const string REPLY_AID = "/KeriAuth/signify/replyAID";
        public const string APP_CLOSED = "/KeriAuth/signify/app_closed";
    }

    /// <summary>
    /// Reply message containing a credential.
    /// Used when user selects and authorizes a credential to be shared with a web page.
    /// </summary>
    public record AppBwReplyCredentialMessage : AppBwMessage {
        public AppBwReplyCredentialMessage(int tabId, string requestId, RecursiveDictionary credential)
            : base(AppBwMessageTypes.REPLY_CREDENTIAL, tabId, requestId, credential) { }
    }

    /// <summary>
    /// Reply message containing an AID (Autonomic Identifier) authorization result.
    /// Used when user selects and authorizes an identifier for a web page.
    /// </summary>
    public record AppBwReplyAidMessage : AppBwMessage {
        public AppBwReplyAidMessage(int tabId, string requestId, AuthorizeResult authorizeResult)
            : base(AppBwMessageTypes.REPLY_AID, tabId, requestId, authorizeResult) { }
    }

    /// <summary>
    /// Reply message containing a sign result.
    /// Used when user approves signing a request with their identifier.
    /// </summary>
    public record AppBwReplySignMessage : AppBwMessage {
        public AppBwReplySignMessage(int tabId, string requestId, ApprovedSignRequest approvedSignRequest)
            : base(AppBwMessageTypes.REPLY_SIGN, tabId, requestId, approvedSignRequest) { }
    }

    /// <summary>
    /// Reply message indicating an error occurred.
    /// </summary>
    public record AppBwReplyErrorMessage : AppBwMessage {
        public AppBwReplyErrorMessage(int tabId, string requestId, string errorMessage)
            : base(AppBwMessageTypes.REPLY_ERROR, tabId, requestId, null, errorMessage) { }
    }

    /// <summary>
    /// Reply message indicating the user canceled the operation.
    /// </summary>
    public record AppBwReplyCanceledMessage : AppBwMessage {
        public AppBwReplyCanceledMessage(int tabId, string requestId, string? errorMessage = null)
            : base(AppBwMessageTypes.REPLY_CANCELED, tabId, requestId, null, errorMessage ?? "User cancelled") { }
    }

    /// <summary>
    /// Message indicating the App (popup/tab) was closed by the user.
    /// </summary>
    public record AppBwAppClosedMessage : AppBwMessage {
        public AppBwAppClosedMessage(int tabId, string requestId)
            : base(AppBwMessageTypes.APP_CLOSED, tabId, requestId) { }
    }

    /// <summary>
    /// Payload for approved sign request.
    /// Contains all information needed to sign an HTTP request.
    /// </summary>
    public record ApprovedSignRequest(
        [property: JsonPropertyName("passcode")] string Passcode,
        [property: JsonPropertyName("adminUrl")] string AdminUrl,
        [property: JsonPropertyName("origin")] string Origin,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("method")] string Method,
        [property: JsonPropertyName("headers")] Dictionary<string, string> Headers,
        [property: JsonPropertyName("identifierName")] string IdentifierName
    );

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
