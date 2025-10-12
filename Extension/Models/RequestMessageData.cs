using System.Text.Json.Serialization;

namespace Extension.Models {
    /// <summary>
    /// Represents a request message conforming to the polaris-web MessageData interface.
    /// Used for incoming messages from web pages via the content script.
    /// </summary>
    /// <typeparam name="T">The type of the payload (e.g., SignDataArgs, AuthorizeArgs)</typeparam>
    public record RequestMessageData<T> {
        [JsonConstructor]
        public RequestMessageData(
            string type,
            string requestId,
            T? payload = default,
            string? error = default,
            string? source = default) {
            Type = type;
            RequestId = requestId;
            Payload = payload;
            Error = error;
            Source = source;
        }

        /// <summary>
        /// Constructor that automatically determines the type based on the payload type.
        /// </summary>
        public RequestMessageData(
            string requestId,
            T? payload = default,
            string? error = default,
            string? source = default) {
            Type = GetMessageTypeFromPayloadType();
            RequestId = requestId;
            Payload = payload;
            Error = error;
            Source = source;
        }

        [JsonPropertyName("type")]
        public string Type { get; }

        private static string GetMessageTypeFromPayloadType() {
            var typeName = typeof(T).Name;
            return typeName switch {
                nameof(SignDataArgs) => "/signify/sign-data",
                nameof(SignRequestArgs) => "/signify/sign-request",
                nameof(AuthorizeArgs) => "/signify/authorize",
                nameof(CreateCredentialArgs) => "/signify/credential/create/data-attestation",
                nameof(GetCredentialArgs) => "/signify/credential/get",
                nameof(ConfigureVendorArgs) => "/signify/configure-vendor",
                _ => throw new InvalidOperationException($"Unknown payload type: {typeName}")
            };
        }

        [JsonPropertyName("requestId")]
        public string RequestId { get; }

        [JsonPropertyName("payload")]
        public T? Payload { get; }

        [JsonPropertyName("error")]
        public string? Error { get; }

        [JsonPropertyName("source")]
        public string? Source { get; }
    }

    /// <summary>
    /// Arguments for signing arbitrary data strings.
    /// Corresponds to SignDataArgs in polaris-web-client.d.ts
    /// </summary>
    public record SignDataArgs(
        [property: JsonPropertyName("items")] string[] Items,
        [property: JsonPropertyName("message")] string? Message = default
    );

    /// <summary>
    /// Arguments for signing HTTP request headers.
    /// Corresponds to SignRequestArgs in polaris-web-client.d.ts
    /// </summary>
    public record SignRequestArgs(
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("method")] string? Method = default,
        [property: JsonPropertyName("headers")] Dictionary<string, string>? Headers = default
    );

    /// <summary>
    /// Session configuration for authorization.
    /// Corresponds to SessionArgs in polaris-web-client.d.ts
    /// </summary>
    public record SessionArgs(
        [property: JsonPropertyName("oneTime")] bool? OneTime = default
    );

    /// <summary>
    /// Arguments for authorization requests.
    /// Corresponds to AuthorizeArgs in polaris-web-client.d.ts
    /// </summary>
    public record AuthorizeArgs(
        [property: JsonPropertyName("message")] string? Message = default,
        [property: JsonPropertyName("session")] SessionArgs? Session = default
    );

    /// <summary>
    /// Arguments for creating data attestation credentials.
    /// Corresponds to CreateCredentialArgs in polaris-web-client.d.ts
    /// </summary>
    public record CreateCredentialArgs(
        [property: JsonPropertyName("credData")] object CredData,
        [property: JsonPropertyName("schemaSaid")] string SchemaSaid
    );

    /// <summary>
    /// Arguments for getting a credential by SAID.
    /// Corresponds to getCredential parameters in polaris-web-client.d.ts
    /// </summary>
    public record GetCredentialArgs(
        [property: JsonPropertyName("said")] string Said,
        [property: JsonPropertyName("includeCESR")] bool? IncludeCESR = default
    );

    /// <summary>
    /// Arguments for configuring vendor settings.
    /// Corresponds to ConfigureVendorArgs in polaris-web-client.d.ts
    /// </summary>
    public record ConfigureVendorArgs(
        [property: JsonPropertyName("url")] string Url
    );
}
