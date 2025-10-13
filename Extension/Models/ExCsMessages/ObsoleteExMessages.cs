using System.Text.Json.Serialization;

namespace Extension.Models.ObsoleteExMessages {
    // NOTE: Message types have been refactored to InboundMessages.cs and OutboundMessages.cs
    // for better type safety and clearer directional flow.
    // This file now only contains shared data structures used in message payloads.

    // Identifier used in responses
    public record PortIdentifier {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("prefix")]
        public string Prefix { get; init; }

        [JsonConstructor]
        public PortIdentifier(string prefix, string? name = null) {
            Prefix = prefix;
            Name = name;
        }
    }

    // Credential used in responses
    public record PortCredential {
        [JsonPropertyName("issueeName")]
        public string IssueeName { get; init; }

        [JsonPropertyName("ancatc")]
        public string[] Ancatc { get; init; }

        [JsonPropertyName("sad")]
        public CredentialSad Sad { get; init; }

        [JsonPropertyName("schema")]
        public CredentialSchema Schema { get; init; }

        [JsonPropertyName("status")]
        public CredentialStatus Status { get; init; }

        [JsonPropertyName("cesr")]
        public string? Cesr { get; init; }

        [JsonConstructor]
        public PortCredential(
            string issueeName,
            string[] ancatc,
            CredentialSad sad,
            CredentialSchema schema,
            CredentialStatus status,
            string? cesr = null) {
            IssueeName = issueeName;
            Ancatc = ancatc;
            Sad = sad;
            Schema = schema;
            Status = status;
            Cesr = cesr;
        }
    }

    // Nested records for PortCredential
    public record CredentialSad {
        [JsonPropertyName("a")]
        public CredentialSadA A { get; init; }

        [JsonPropertyName("d")]
        public string D { get; init; }

        [JsonConstructor]
        public CredentialSad(CredentialSadA a, string d) {
            A = a;
            D = d;
        }
    }

    public record CredentialSadA {
        [JsonPropertyName("i")]
        public string I { get; init; }

        [JsonConstructor]
        public CredentialSadA(string i) {
            I = i;
        }
    }

    public record CredentialSchema {
        [JsonPropertyName("title")]
        public string Title { get; init; }

        [JsonPropertyName("credentialType")]
        public string CredentialType { get; init; }

        [JsonPropertyName("description")]
        public string Description { get; init; }

        [JsonConstructor]
        public CredentialSchema(string title, string credentialType, string description) {
            Title = title;
            CredentialType = credentialType;
            Description = description;
        }
    }

    public record CredentialStatus {
        [JsonPropertyName("et")]
        public string Et { get; init; }

        [JsonConstructor]
        public CredentialStatus(string et) {
            Et = et;
        }
    }

    // Signature used in responses
    public record PortSignature {
        [JsonPropertyName("headers")]
        public Dictionary<string, string> Headers { get; init; }

        [JsonPropertyName("credential")]
        public PortCredential? Credential { get; init; }

        [JsonPropertyName("identifier")]
        public PortIdentifierSimple? Identifier { get; init; }

        [JsonPropertyName("autoSignin")]
        public bool? AutoSignin { get; init; }

        [JsonConstructor]
        public PortSignature(
            Dictionary<string, string> headers,
            PortCredential? credential = null,
            PortIdentifierSimple? identifier = null,
            bool? autoSignin = null) {
            Headers = headers;
            Credential = credential;
            Identifier = identifier;
            AutoSignin = autoSignin;
        }
    }

    // Simplified identifier for PortSignature
    public record PortIdentifierSimple {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("prefix")]
        public string? Prefix { get; init; }

        [JsonConstructor]
        public PortIdentifierSimple(string? prefix = null, string? name = null) {
            Prefix = prefix;
            Name = name;
        }
    }

    // Generic reply message data (already exists in ReplyMessageData.cs, but including for reference)
    // This corresponds to IReplyMessageData<T> in TypeScript

    // Content Script to Page message tag
    public static class CsPageConstants {
        public const string CsPageMsgTag = "KeriAuthCs";
    }

    // Content Script to Page message data
    public record CsPageMsgData<T> {
        [JsonPropertyName("type")]
        public string Type { get; init; }

        [JsonPropertyName("requestId")]
        public string RequestId { get; init; }

        [JsonPropertyName("payload")]
        public T? Payload { get; init; }

        [JsonPropertyName("error")]
        public object? Error { get; init; }

        [JsonPropertyName("source")]
        public string Source { get; init; }

        [JsonConstructor]
        public CsPageMsgData(
            string type,
            string requestId,
            string source,
            T? payload = default,
            object? error = null) {
            Type = type;
            RequestId = requestId;
            Source = source;
            Payload = payload;
            Error = error;
        }
    }

    // Content Script to Page message with data property instead of payload
    public record CsPageMsgDataData<T> {
        [JsonPropertyName("type")]
        public string Type { get; init; }

        [JsonPropertyName("requestId")]
        public string RequestId { get; init; }

        [JsonPropertyName("data")]
        public T? Data { get; init; }

        [JsonPropertyName("error")]
        public object? Error { get; init; }

        [JsonPropertyName("source")]
        public string Source { get; init; }

        [JsonConstructor]
        public CsPageMsgDataData(
            string type,
            string requestId,
            string source,
            T? data = default,
            object? error = null) {
            Type = type;
            RequestId = requestId;
            Source = source;
            Data = data;
            Error = error;
        }
    }

    // Signin data structure
    public record Signin {
        [JsonPropertyName("id")]
        public string Id { get; init; }

        [JsonPropertyName("domain")]
        public string Domain { get; init; }

        [JsonPropertyName("identifier")]
        public PortIdentifierSimple? Identifier { get; init; }

        [JsonPropertyName("credential")]
        public PortCredential? Credential { get; init; }

        [JsonPropertyName("createdAt")]
        public long CreatedAt { get; init; }

        [JsonPropertyName("updatedAt")]
        public long UpdatedAt { get; init; }

        [JsonPropertyName("autoSignin")]
        public bool? AutoSignin { get; init; }

        [JsonConstructor]
        public Signin(
            string id,
            string domain,
            long createdAt,
            long updatedAt,
            PortIdentifierSimple? identifier = null,
            PortCredential? credential = null,
            bool? autoSignin = null) {
            Id = id;
            Domain = domain;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
            Identifier = identifier;
            Credential = credential;
            AutoSignin = autoSignin;
        }
    }

    // Updated ApprovedSignRequest for port messages (similar to existing but matching TypeScript interface exactly)
    public record PortApprovedSignRequest {
        [JsonPropertyName("originStr")]
        public string OriginStr { get; init; }

        [JsonPropertyName("url")]
        public string Url { get; init; }

        [JsonPropertyName("method")]
        public string Method { get; init; }

        [JsonPropertyName("initHeadersDict")]
        public Dictionary<string, string>? InitHeadersDict { get; init; }

        [JsonPropertyName("selectedName")]
        public string SelectedName { get; init; }

        [JsonConstructor]
        public PortApprovedSignRequest(
            string originStr,
            string url,
            string method,
            string selectedName,
            Dictionary<string, string>? initHeadersDict = null) {
            OriginStr = originStr;
            Url = url;
            Method = method;
            SelectedName = selectedName;
            InitHeadersDict = initHeadersDict;
        }
    }
}
