using System.Text.Json.Serialization;

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
}

/// <summary>
/// RPC response message sent in reply to an RpcRequest.
/// </summary>
public record RpcResponse : PortMessage {
    [JsonPropertyName("t")]
    public override string T => "RPC_RES";

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
