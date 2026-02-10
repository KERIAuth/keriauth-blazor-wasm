using System.Text.Json;
using System.Text.Json.Serialization;
using Extension.Helper;

namespace Extension.Models.Messages.Port;

/// <summary>
/// Context types for port connections.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContextKind>))]
public enum ContextKind {
    [JsonStringEnumMemberName("content-script")]
    ContentScript,

    [JsonStringEnumMemberName("extension-app")]
    ExtensionApp
}

/// <summary>
/// Base record for all port-based messages.
/// The 't' field discriminates message types for routing.
/// </summary>
public abstract record PortMessage {
    [JsonPropertyName("t")]
    public abstract string T { get; }
}

/// <summary>
/// Sent by CS or App immediately after connecting to establish a PortSession.
/// </summary>
public record HelloMessage : PortMessage {
    [JsonPropertyName("t")]
    public override string T => "HELLO";

    [JsonPropertyName("context")]
    public required ContextKind Context { get; init; }

    [JsonPropertyName("instanceId")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("tabId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TabId { get; init; }

    [JsonPropertyName("frameId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FrameId { get; init; }
}

/// <summary>
/// Sent by BW in response to HELLO to confirm PortSession establishment.
/// </summary>
public record ReadyMessage : PortMessage {
    [JsonPropertyName("t")]
    public override string T => "READY";

    [JsonPropertyName("portSessionId")]
    public required string PortSessionId { get; init; }

    [JsonPropertyName("tabId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TabId { get; init; }

    [JsonPropertyName("frameId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FrameId { get; init; }
}

/// <summary>
/// Sent by App to associate itself with a specific tab's PortSession.
/// </summary>
public record AttachTabMessage : PortMessage {
    [JsonPropertyName("t")]
    public override string T => "ATTACH_TAB";

    [JsonPropertyName("tabId")]
    public required int TabId { get; init; }

    [JsonPropertyName("frameId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FrameId { get; init; }
}

/// <summary>
/// Sent by App to detach from its currently attached tab's PortSession.
/// </summary>
public record DetachTabMessage : PortMessage {
    [JsonPropertyName("t")]
    public override string T => "DETACH_TAB";
}

/// <summary>
/// One-way event message for notifications that don't require a response.
/// </summary>
public record EventMessage : PortMessage {
    [JsonPropertyName("t")]
    public override string T => "EVENT";

    [JsonPropertyName("portSessionId")]
    public required string PortSessionId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; init; }
}

/// <summary>
/// RPC request message for request-response patterns.
/// </summary>
public record RpcRequest : PortMessage {
    [JsonPropertyName("t")]
    public override string T => "RPC_REQ";

    [JsonPropertyName("portSessionId")]
    public required string PortSessionId { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Params { get; init; }

    /// <summary>
    /// Deserializes Params to the specified type.
    /// Returns null if Params is null or deserialization fails.
    /// </summary>
    /// <typeparam name="T">The target type for deserialization</typeparam>
    /// <returns>The deserialized params, or null if unavailable or invalid</returns>
    public T? GetParams<T>() where T : class {
        if (Params is null) return null;
        if (Params is JsonElement el) {
            return JsonSerializer.Deserialize<T>(el.GetRawText(), JsonOptions.Default);
        }
        // If already deserialized to correct type
        if (Params is T typed) return typed;
        return null;
    }
}

/// <summary>
/// RPC response message sent in reply to an RpcRequest.
/// Supports both generic ('RPC_RES') and directional ('BW_CS_RPC_RES') discriminators.
/// </summary>
public record RpcResponse : PortMessage {
    [JsonPropertyName("t")]
    public override string T => Discriminator;

    /// <summary>
    /// The message type discriminator value. Defaults to generic 'RPC_RES'.
    /// Set to CsBwPortMessageTypes.RpcResponse for BW→CS responses.
    /// </summary>
    [JsonIgnore]
    public string Discriminator { get; init; } = PortMessageTypes.RpcResponse;

    [JsonPropertyName("portSessionId")]
    public required string PortSessionId { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("ok")]
    public required bool Ok { get; init; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Result { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
}

/// <summary>
/// Error response sent when an operation fails (e.g., ATTACH_TAB to non-existent session).
/// </summary>
public record ErrorMessage : PortMessage {
    [JsonPropertyName("t")]
    public override string T => "ERROR";

    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }
}

/// <summary>
/// Known error codes for ErrorMessage.
/// </summary>
public static class PortErrorCodes {
    public const string AttachFailed = "ATTACH_FAILED";
    public const string InvalidMessage = "INVALID_MESSAGE";
    public const string SessionNotFound = "SESSION_NOT_FOUND";
}

/// <summary>
/// Generic port message type discriminators.
/// </summary>
public static class PortMessageTypes {
    public const string Hello = "HELLO";
    public const string Ready = "READY";
    public const string AttachTab = "ATTACH_TAB";
    public const string DetachTab = "DETACH_TAB";
    public const string Event = "EVENT";
    public const string RpcRequest = "RPC_REQ";
    public const string RpcResponse = "RPC_RES";
    public const string Error = "ERROR";
    public const string Heartbeat = "BW_HEARTBEAT";
}

/// <summary>
/// Message type discriminators for chrome.runtime.sendMessage (non-port) communication.
/// Used for WASM readiness probing before port creation.
/// </summary>
public static class SendMessageTypes {
    /// <summary>Client → SW: "Is WASM alive?" (sent via chrome.runtime.sendMessage)</summary>
    public const string ClientHello = "CLIENT_SW_HELLO";
    /// <summary>SW → Client: "WASM is ready, you can create a port" (sent via runtime/tabs.sendMessage)</summary>
    public const string SwHello = "SW_CLIENT_HELLO";
    /// <summary>SW → App: "I have pending work, please reconnect" (sent via chrome.runtime.sendMessage)</summary>
    public const string SwAppWake = "SW_APP_WAKE";
}

/// <summary>
/// Directional port message type discriminators for CS→BW communication.
/// These help identify message direction in logs.
/// </summary>
public static class CsBwPortMessageTypes {
    /// <summary>RPC request from ContentScript to BackgroundWorker</summary>
    public const string RpcRequest = "CS_BW_RPC_REQ";
    /// <summary>RPC response from BackgroundWorker to ContentScript</summary>
    public const string RpcResponse = "BW_CS_RPC_RES";
}
