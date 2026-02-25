using System.Text.Json;
using System.Text.Json.Serialization;
using Extension.Helper;
using Extension.Models.Messages.Common;
using Extension.Services.SignifyService.Models;

namespace Extension.Models.Messages.AppBw {
    /// <summary>
    /// Non-generic version of AppBwMessage for initial deserialization when payload type is unknown.
    /// Direction: App → BackgroundWorker
    /// Contains App-specific properties (TabId, TabUrl, Error) with JsonElement? Payload for two-phase deserialization.
    /// Use to inspect Type property, then convert to typed AppBwMessage if needed.
    /// </summary>
    public record AppBwMessage : ToBwMessage {
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
        public AppBwMessage(string type, int tabId = 0, string? tabUrl = null, string? requestId = null, JsonElement? payload = null, string? error = null)
            : base(type, requestId, payload) {
            TabId = tabId;
            TabUrl = tabUrl;
            Error = error;
        }

        /// <summary>
        /// Converts this non-generic message to a typed AppBwMessage.
        /// Deserializes the Payload JsonElement to the specified type using the provided options.
        /// </summary>
        public AppBwMessage<T> ToTyped<T>(JsonSerializerOptions? options = null) {
            T? typedPayload = default;
            if (Payload.HasValue && Payload.Value.ValueKind != JsonValueKind.Null) {
                typedPayload = JsonSerializer.Deserialize<T>(Payload.Value.GetRawText(), options);
            }
            return new AppBwMessage<T>(Type, TabId, TabUrl, RequestId, typedPayload, Error);
        }
    }

    /// <summary>
    /// Generic version of AppBwMessage with typed payload.
    /// Direction: App → BackgroundWorker
    /// Extends ToBwMessage with App-specific properties (TabId, TabUrl, Error).
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
            public const string ReplySignData = "AppBw.ReplySignData";
            public const string ReplyCreateCredential = "AppBw.ReplyCreateCredential";
            public const string AppClosed = "AppBw.AppClosed";
            public const string UserActivity = "AppBw.UserActivity";
            public const string RequestAddIdentifier = "AppBw.RequestAddIdentifier";
            /// <summary>
            /// Response from App to a BW-initiated request.
            /// </summary>
            public const string ResponseToBwRequest = "AppBw.ResponseToBwRequest";
            /// <summary>
            /// Request to check KERIA health status.
            /// </summary>
            public const string RequestHealthCheck = "AppBw.RequestHealthCheck";
            /// <summary>
            /// Request to connect to KERIA.
            /// </summary>
            public const string RequestConnect = "AppBw.RequestConnect";
            /// <summary>
            /// Request to create a new AID.
            /// </summary>
            public const string RequestCreateAid = "AppBw.RequestCreateAid";
            /// <summary>
            /// Request to get credentials from KERIA.
            /// </summary>
            public const string RequestGetCredentials = "AppBw.RequestGetCredentials";
            /// <summary>
            /// App user approved sign-in with AID (and optionally credential SAID).
            /// BackgroundWorker will dereference the credential and get CESR if needed.
            /// </summary>
            public const string ReplyAidApproval = "AppBw.ReplyAidApproval";
            /// <summary>
            /// App user approved signing data items with the selected identifier.
            /// BackgroundWorker will perform the actual signing via signify-ts.
            /// </summary>
            public const string ReplySignDataApproval = "AppBw.ReplySignDataApproval";
            /// <summary>
            /// Request to get key state for an identifier.
            /// </summary>
            public const string RequestGetKeyState = "AppBw.RequestGetKeyState";
            /// <summary>
            /// Request to get key events for an identifier.
            /// </summary>
            public const string RequestGetKeyEvents = "AppBw.RequestGetKeyEvents";
            /// <summary>
            /// Request to rename an AID.
            /// </summary>
            public const string RequestRenameAid = "AppBw.RequestRenameAid";
            /// <summary>
            /// Request to execute PrimeData Go action (create GEDA/QVI/LE/PERSON AIDs).
            /// </summary>
            public const string RequestPrimeDataGo = "AppBw.RequestPrimeDataGo";
            public const string RequestGetOobi = "AppBw.RequestGetOobi";
            public const string RequestResolveOobi = "AppBw.RequestResolveOobi";
            /// <summary>
            /// App user approved a connection invite and selected an AID.
            /// BackgroundWorker will generate its OOBI for the selected AID and send it to the page.
            /// </summary>
            public const string ReplyConnectionInvite = "AppBw.ReplyConnectionInvite";
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
        public static AppBwMessageType ReplySignData { get; } = new(Values.ReplySignData);
        public static AppBwMessageType ReplyCreateCredential { get; } = new(Values.ReplyCreateCredential);
        public static AppBwMessageType AppClosed { get; } = new(Values.AppClosed);
        public static AppBwMessageType UserActivity { get; } = new(Values.UserActivity);
        public static AppBwMessageType RequestAddIdentifier { get; } = new(Values.RequestAddIdentifier);
        public static AppBwMessageType ResponseToBwRequest { get; } = new(Values.ResponseToBwRequest);
        public static AppBwMessageType RequestHealthCheck { get; } = new(Values.RequestHealthCheck);
        public static AppBwMessageType RequestConnect { get; } = new(Values.RequestConnect);
        public static AppBwMessageType RequestCreateAid { get; } = new(Values.RequestCreateAid);
        public static AppBwMessageType RequestGetCredentials { get; } = new(Values.RequestGetCredentials);
        public static AppBwMessageType ReplyAidApproval { get; } = new(Values.ReplyAidApproval);
        public static AppBwMessageType ReplySignDataApproval { get; } = new(Values.ReplySignDataApproval);
        public static AppBwMessageType RequestGetKeyState { get; } = new(Values.RequestGetKeyState);
        public static AppBwMessageType RequestGetKeyEvents { get; } = new(Values.RequestGetKeyEvents);
        public static AppBwMessageType RequestRenameAid { get; } = new(Values.RequestRenameAid);
        public static AppBwMessageType RequestPrimeDataGo { get; } = new(Values.RequestPrimeDataGo);
        public static AppBwMessageType RequestGetOobi { get; } = new(Values.RequestGetOobi);
        public static AppBwMessageType RequestResolveOobi { get; } = new(Values.RequestResolveOobi);
        public static AppBwMessageType ReplyConnectionInvite { get; } = new(Values.ReplyConnectionInvite);

        /// <summary>
        /// Parse a string value into an AppBwMessageType.
        /// Use this when deserializing from JSON or other external sources.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when the value is not a valid message type.</exception>
        public static AppBwMessageType Parse(string value) {
            if (TryParse(value, out var result)) {
                return result;
            }
            throw new ArgumentException($"Invalid AppBwMessageType: '{value}'", nameof(value));
        }

        /// <summary>
        /// Try to parse a string value into an AppBwMessageType.
        /// Returns true if successful, false if the value is not a valid message type.
        /// </summary>
        public static bool TryParse(string? value, out AppBwMessageType result) {
            result = default;
            if (value is null) return false;

            switch (value) {
                case Values.ReplyCredential:
                    result = ReplyCredential;
                    return true;
                case Values.ReplyCanceled:
                    result = ReplyCanceled;
                    return true;
                case Values.ReplyError:
                    result = ReplyError;
                    return true;
                case Values.ReplyIdentifier:
                    result = ReplyIdentifier;
                    return true;
                case Values.ReplyAid:
                    result = ReplyAid;
                    return true;
                case Values.ReplyApprovedSignHeaders:
                    result = ReplyApprovedSignHeaders;
                    return true;
                case Values.ReplySignData:
                    result = ReplySignData;
                    return true;
                case Values.ReplyCreateCredential:
                    result = ReplyCreateCredential;
                    return true;
                case Values.AppClosed:
                    result = AppClosed;
                    return true;
                case Values.UserActivity:
                    result = UserActivity;
                    return true;
                case Values.RequestAddIdentifier:
                    result = RequestAddIdentifier;
                    return true;
                case Values.ResponseToBwRequest:
                    result = ResponseToBwRequest;
                    return true;
                case Values.RequestHealthCheck:
                    result = RequestHealthCheck;
                    return true;
                case Values.RequestConnect:
                    result = RequestConnect;
                    return true;
                case Values.RequestCreateAid:
                    result = RequestCreateAid;
                    return true;
                case Values.RequestGetCredentials:
                    result = RequestGetCredentials;
                    return true;
                case Values.ReplyAidApproval:
                    result = ReplyAidApproval;
                    return true;
                case Values.ReplySignDataApproval:
                    result = ReplySignDataApproval;
                    return true;
                case Values.RequestGetKeyState:
                    result = RequestGetKeyState;
                    return true;
                case Values.RequestGetKeyEvents:
                    result = RequestGetKeyEvents;
                    return true;
                case Values.RequestRenameAid:
                    result = RequestRenameAid;
                    return true;
                case Values.RequestPrimeDataGo:
                    result = RequestPrimeDataGo;
                    return true;
                case Values.RequestGetOobi:
                    result = RequestGetOobi;
                    return true;
                case Values.RequestResolveOobi:
                    result = RequestResolveOobi;
                    return true;
                case Values.ReplyConnectionInvite:
                    result = ReplyConnectionInvite;
                    return true;
                default:
                    return false;
            }
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
    /// Reply message from App containing user's sign approval or rejection.
    /// Contains the URL, method, headers, and identifier prefix needed for signing.
    /// </summary>
    public record AppBwReplySignMessage : AppBwMessage<AppBwReplySignPayload2> {
        public AppBwReplySignMessage(int tabId, string? tabUrl, string requestId, string origin, string url, string method, Dictionary<string, string> headers, string prefix)
            : base(AppBwMessageType.ReplyApprovedSignHeaders, tabId, tabUrl, requestId, payload: new AppBwReplySignPayload2(origin, url, method, headers, prefix)) { }
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

    /// <summary>
    /// A single signed data item containing the original data and its signature.
    /// </summary>
    public record SignDataResultItem(
        [property: JsonPropertyName("data")] string Data,
        [property: JsonPropertyName("signature")] string Signature
    );

    /// <summary>
    /// Result of signing data items, containing the AID prefix and array of signed items.
    /// Matches the polaris-web SignDataResult interface.
    /// </summary>
    public record SignDataResult(
        [property: JsonPropertyName("aid")] string Aid,
        [property: JsonPropertyName("items")] SignDataResultItem[] Items
    );

    /// <summary>
    /// Reply message containing signed data results.
    /// Used when user approves signing data items with their identifier.
    /// </summary>
    public record AppBwReplySignDataMessage : AppBwMessage<SignDataResult> {
        public AppBwReplySignDataMessage(int tabId, string? tabUrl, string requestId, SignDataResult signDataResult)
            : base(AppBwMessageType.ReplySignData, tabId, tabUrl, requestId, signDataResult) { }
    }

    /// <summary>
    /// Payload sent from App to BackgroundWorker when user approves credential creation.
    /// Contains the selected identifier and the credential data to be attested.
    /// BackgroundWorker will use this to issue the credential via signify-ts.
    /// </summary>
    public record CreateCredentialApprovalPayload(
        [property: JsonPropertyName("aidName")] string AidName,
        [property: JsonPropertyName("aidPrefix")] string AidPrefix,
        [property: JsonPropertyName("credData")] object CredData,
        [property: JsonPropertyName("schemaSaid")] string SchemaSaid
    );

    /// <summary>
    /// Result of creating a data attestation credential.
    /// Matches the polaris-web CreateCredentialResult interface.
    /// Contains the ACDC, issuance event (iss), anchor event (anc), and operation (op).
    /// </summary>
    public record CreateCredentialResult(
        [property: JsonPropertyName("acdc")] RecursiveDictionary Acdc,
        [property: JsonPropertyName("iss")] RecursiveDictionary Iss,
        [property: JsonPropertyName("anc")] RecursiveDictionary Anc,
        [property: JsonPropertyName("op")] RecursiveDictionary Op
    );

    /// <summary>
    /// Reply message containing the created credential result.
    /// Used when user approves creating a data attestation credential.
    /// </summary>
    public record AppBwReplyCreateCredentialMessage : AppBwMessage<CreateCredentialResult> {
        public AppBwReplyCreateCredentialMessage(int tabId, string? tabUrl, string requestId, CreateCredentialResult createCredentialResult)
            : base(AppBwMessageType.ReplyCreateCredential, tabId, tabUrl, requestId, createCredentialResult) { }
    }

    #region App→BW Request Payloads for SignifyClientService Operations

    /// <summary>
    /// Payload for health check request from App to BackgroundWorker.
    /// </summary>
    public record HealthCheckRequestPayload(
        [property: JsonPropertyName("healthUrl")] string HealthUrl
    );

    /// <summary>
    /// Response payload for health check request.
    /// </summary>
    public record HealthCheckResponsePayload(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("error")] string? Error = null
    );

    /// <summary>
    /// Payload for connect request from App to BackgroundWorker.
    /// </summary>
    public record ConnectRequestPayload(
        [property: JsonPropertyName("adminUrl")] string AdminUrl,
        [property: JsonPropertyName("passcode")] string Passcode,
        [property: JsonPropertyName("bootUrl")] string? BootUrl,
        [property: JsonPropertyName("isNewAgent")] bool IsNewAgent,
        [property: JsonPropertyName("passcodeHash")] int PasscodeHash
    );

    /// <summary>
    /// Response payload for connect request.
    /// Contains the controller and agent prefixes on success.
    /// </summary>
    public record ConnectResponsePayload(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("clientAidPrefix")] string? ClientAidPrefix = null,
        [property: JsonPropertyName("agentAidPrefix")] string? AgentAidPrefix = null,
        [property: JsonPropertyName("error")] string? Error = null
    );

    /// <summary>
    /// Payload for create AID request from App to BackgroundWorker.
    /// </summary>
    public record CreateAidRequestPayload(
        [property: JsonPropertyName("alias")] string Alias
    );

    /// <summary>
    /// Response payload for create AID request.
    /// </summary>
    public record CreateAidResponsePayload(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("prefix")] string? Prefix = null,
        [property: JsonPropertyName("error")] string? Error = null
    );

    /// <summary>
    /// Response payload for get credentials request.
    /// Contains list of credentials from KERIA.
    /// </summary>
    public record GetCredentialsResponsePayload(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("credentials")] List<RecursiveDictionary>? Credentials = null,
        [property: JsonPropertyName("error")] string? Error = null
    );

    /// <summary>
    /// Payload for AID approval from App to BackgroundWorker.
    /// App sends the user's selected identifier and optionally a credential SAID.
    /// BackgroundWorker will dereference the credential and fetch CESR representation if needed.
    /// </summary>
    public record AidApprovalPayload(
        [property: JsonPropertyName("prefix")] string Prefix,
        [property: JsonPropertyName("alias")] string Alias,
        [property: JsonPropertyName("credentialSaid")] string? CredentialSaid = null
    );

    /// <summary>
    /// Reply message for AID approval (sign-in with identifier and optional credential).
    /// App sends this when user approves sign-in; BW handles credential dereferencing.
    /// </summary>
    public record AppBwReplyAidApprovalMessage : AppBwMessage<AidApprovalPayload> {
        public AppBwReplyAidApprovalMessage(int tabId, string? tabUrl, string requestId, AidApprovalPayload payload)
            : base(AppBwMessageType.ReplyAidApproval, tabId, tabUrl, requestId, payload) { }
    }

    /// <summary>
    /// Payload for sign-data approval from App to BackgroundWorker.
    /// App sends the user's selected identifier prefix and the data items to sign.
    /// BackgroundWorker will perform the actual signing via signify-ts.
    /// </summary>
    public record SignDataApprovalPayload(
        [property: JsonPropertyName("prefix")] string Prefix,
        [property: JsonPropertyName("dataItems")] string[] DataItems
    );

    /// <summary>
    /// Reply message for sign-data approval.
    /// App sends this when user approves signing data; BW handles the actual signing.
    /// </summary>
    public record AppBwReplySignDataApprovalMessage : AppBwMessage<SignDataApprovalPayload> {
        public AppBwReplySignDataApprovalMessage(int tabId, string? tabUrl, string requestId, SignDataApprovalPayload payload)
            : base(AppBwMessageType.ReplySignDataApproval, tabId, tabUrl, requestId, payload) { }
    }

    /// <summary>
    /// Payload for get key state request from App to BackgroundWorker.
    /// </summary>
    public record GetKeyStateRequestPayload(
        [property: JsonPropertyName("prefix")] string Prefix
    );

    /// <summary>
    /// Response payload for get key state request.
    /// Contains the key state data on success.
    /// </summary>
    public record GetKeyStateResponsePayload(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("keyState")] KeyState? KeyState = null,
        [property: JsonPropertyName("error")] string? Error = null
    );

    /// <summary>
    /// Payload for get key events request from App to BackgroundWorker.
    /// </summary>
    public record GetKeyEventsRequestPayload(
        [property: JsonPropertyName("prefix")] string Prefix
    );

    /// <summary>
    /// Response payload for get key events request.
    /// Contains the key events data on success.
    /// </summary>
    public record GetKeyEventsResponsePayload(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("keyEvents")] RecursiveDictionary? KeyEvents = null,
        [property: JsonPropertyName("error")] string? Error = null
    );

    /// <summary>
    /// Payload for rename AID request from App to BackgroundWorker.
    /// </summary>
    public record RenameAidRequestPayload(
        [property: JsonPropertyName("currentName")] string CurrentName,
        [property: JsonPropertyName("newName")] string NewName
    );

    /// <summary>
    /// Response payload for rename AID request.
    /// </summary>
    public record RenameAidResponsePayload(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("error")] string? Error = null
    );

    /// <summary>
    /// Payload for PrimeData Go request from App to BackgroundWorker.
    /// Expandable: add more fields later (salts, registry names, etc.)
    /// </summary>
    public record PrimeDataGoPayload(
        [property: JsonPropertyName("prepend")] string Prepend
    );

    /// <summary>
    /// Response payload for PrimeData Go request.
    /// </summary>
    public record PrimeDataGoResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("error")] string? Error = null
    );

    #endregion
}
