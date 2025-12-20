using System.Text.Json.Serialization;
using Extension.Helper;
using Extension.Models.Messages.Common;
using Extension.Services.SignifyService.Models;

namespace Extension.Models.Messages.AppBw {
    /// <summary>
    /// Base record for all messages sent from App (popup/tab/sidepanel) to BackgroundWorker.
    /// Direction: App → BackgroundWorker
    /// Extends ToBwMessage with App-specific properties (TabId, TabUrl, Error).
    /// Not abstract, because it is helpful as first step of instantiating more specific type.
    /// </summary>
    public record AppBwMessage<T> : ToBwMessage<T> {
        [JsonPropertyName("tabId")]
        public int TabId { get; init; }

        [JsonPropertyName("tabUrl")]
        public string? TabUrl { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }

        /// <summary>
        /// Constructor for JSON deserialization.
        /// </summary>
        [JsonConstructor]
        public AppBwMessage(string type, int tabId, string? tabUrl, string? requestId = null, T? payload = default, string? error = null)
            : base(type, requestId, payload) {
            TabId = tabId;
            TabUrl = tabUrl;
            Error = error;
        }

        /// <summary>
        /// Constructor for compile-time type-safe message creation.
        /// </summary>
        public AppBwMessage(AppBwMessageType type, int tabId, string? tabUrl, string? requestId = null, T? payload = default, string? error = null)
            : this(type.Value, tabId, tabUrl, requestId, payload, error) { }
    }

    /// <summary>
    /// Strongly-typed message type that must be one of the defined values.
    /// Use the static properties (e.g., AppBwMessageType.ReplyCredential) for compile-time safety.
    /// Use the Values nested class for switch case labels (e.g., AppBwMessageType.Values.ReplyCredential).
    /// </summary>
    public readonly struct AppBwMessageType : IEquatable<AppBwMessageType> {
        /// <summary>
        /// Constant string values for use in switch case labels.
        /// </summary>
        public static class Values {
            public const string ReplyCredential = "AppBw.ReplyCredential";
            public const string ReplyCanceled = "AppBw.ReplyCanceled";
            public const string ReplyError = "AppBw.ReplyError";
            public const string ReplyIdentifier = "AppBw.ReplyIdentifier";
            public const string ReplyAid = "AppBw.ReplyAid";
            public const string ReplyApprovedSignHeaders = "AppBw.ReplyApprovedSignHeaders";
            public const string AppClosed = "AppBw.AppClosed";
            public const string UserActivity = "AppBw.UserActivity";
            public const string RequestAddIdentifier = "AppBw.RequestAddIdentifier";
            /// <summary>
            /// Response from App to a BW-initiated request.
            /// </summary>
            public const string ResponseToBwRequest = "AppBw.ResponseToBwRequest";
        }

        public string Value { get; }

        private AppBwMessageType(string value) => Value = value;

        // Static properties for compile-time type safety
        public static AppBwMessageType ReplyCredential { get; } = new(Values.ReplyCredential);
        public static AppBwMessageType ReplyCanceled { get; } = new(Values.ReplyCanceled);
        public static AppBwMessageType ReplyError { get; } = new(Values.ReplyError);
        public static AppBwMessageType ReplyIdentifier { get; } = new(Values.ReplyIdentifier);
        public static AppBwMessageType ReplyAid { get; } = new(Values.ReplyAid);
        public static AppBwMessageType ReplyApprovedSignHeaders { get; } = new(Values.ReplyApprovedSignHeaders);
        public static AppBwMessageType AppClosed { get; } = new(Values.AppClosed);
        public static AppBwMessageType UserActivity { get; } = new(Values.UserActivity);
        public static AppBwMessageType RequestAddIdentifier { get; } = new(Values.RequestAddIdentifier);
        public static AppBwMessageType ResponseToBwRequest { get; } = new(Values.ResponseToBwRequest);

        /// <summary>
        /// Parse a string value into an AppBwMessageType.
        /// Use this when deserializing from JSON or other external sources.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when the value is not a valid message type.</exception>
        public static AppBwMessageType Parse(string value) {
            return value switch {
                Values.ReplyCredential => ReplyCredential,
                Values.ReplyCanceled => ReplyCanceled,
                Values.ReplyError => ReplyError,
                Values.ReplyIdentifier => ReplyIdentifier,
                Values.ReplyAid => ReplyAid,
                Values.ReplyApprovedSignHeaders => ReplyApprovedSignHeaders,
                Values.AppClosed => AppClosed,
                Values.UserActivity => UserActivity,
                Values.RequestAddIdentifier => RequestAddIdentifier,
                Values.ResponseToBwRequest => ResponseToBwRequest,
                _ => throw new ArgumentException($"Invalid AppBwMessageType: '{value}'", nameof(value))
            };
        }

        public static implicit operator string(AppBwMessageType type) => type.Value;
        public override string ToString() => Value;

        public bool Equals(AppBwMessageType other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is AppBwMessageType other && Equals(other);
        public override int GetHashCode() => Value?.GetHashCode() ?? 0;
        public static bool operator ==(AppBwMessageType left, AppBwMessageType right) => left.Equals(right);
        public static bool operator !=(AppBwMessageType left, AppBwMessageType right) => !left.Equals(right);
    }

    /// <summary>
    /// Reply message containing a credential.
    /// Used when user selects and authorizes a credential to be shared with a web page.
    /// </summary>
    public record AppBwReplyCredentialMessage : AppBwMessage<RecursiveDictionary> {
        public AppBwReplyCredentialMessage(int tabId, string? tabUrl, string requestId, RecursiveDictionary credential)
            : base(AppBwMessageType.ReplyCredential, tabId, tabUrl, requestId, credential) { }
    }

    /// <summary>
    /// Reply message containing an AID (Autonomic Identifier) authorization result.
    /// Used when user selects and authorizes an identifier for a web page.
    /// </summary>
    public record AppBwReplyAidMessage : AppBwMessage<AuthorizeResult> {
        public AppBwReplyAidMessage(int tabId, string? tabUrl, string requestId, AuthorizeResult authorizeResult)
            : base(AppBwMessageType.ReplyAid, tabId, tabUrl, requestId, authorizeResult) { }
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
            : base(AppBwMessageType.ReplyApprovedSignHeaders, tabId, tabUrl, requestId, payload: new AppBwReplySignPayload1(headersDict, prefix, isApproved)) { }
    }

    /// <summary>
    /// Reply message indicating an error occurred.
    /// </summary>
    public record AppBwReplyErrorMessage : AppBwMessage<object> {
        public AppBwReplyErrorMessage(int tabId, string? tabUrl, string requestId, string errorMessage)
            : base(AppBwMessageType.ReplyError, tabId, tabUrl, requestId, new object(), errorMessage) { }
    }

    /// <summary>
    /// Reply message indicating the user canceled the operation.
    /// </summary>
    public record AppBwReplyCanceledMessage : AppBwMessage<object> {
        public AppBwReplyCanceledMessage(int tabId, string? tabUrl, string requestId, string? errorMessage = null)
            : base(AppBwMessageType.ReplyCanceled, tabId, tabUrl, requestId, new object(), errorMessage ?? "User cancelled") { }
    }

    public record AppBwUserActivityMessage : AppBwMessage<object> {
        public AppBwUserActivityMessage()
            : base(AppBwMessageType.UserActivity, 0, null, string.Empty, new object()) { }
    }

    /// <summary>
    /// Message indicating the App (popup/tab) was closed by the user.
    /// </summary>
    public record AppBwAppClosedMessage : AppBwMessage<object> {
        public AppBwAppClosedMessage(int tabId, string? tabUrl, string requestId)
            : base(AppBwMessageType.AppClosed, tabId, tabUrl, requestId) { }
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

    /// <summary>
    /// Message sent from App to BackgroundWorker in response to a BW-initiated request.
    /// Contains a generic payload that can be deserialized to the expected response type.
    /// </summary>
    public record AppBwResponseToBwRequestMessage : AppBwMessage<object> {
        public AppBwResponseToBwRequestMessage(int tabId, string? tabUrl, string requestId, object? responsePayload)
            : base(AppBwMessageType.ResponseToBwRequest, tabId, tabUrl, requestId, responsePayload) { }
    }
}
