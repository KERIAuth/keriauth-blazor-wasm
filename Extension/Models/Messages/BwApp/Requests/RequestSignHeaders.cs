using System.Text.Json.Serialization;

namespace Extension.Models.Messages.BwApp.Requests;

/// <summary>
/// Payload for BW→App SignHeaders request.
/// Contains the HTTP request details that need to be signed,
/// plus the original ContentScript request details needed for response routing.
/// </summary>
/// <param name="Origin">The origin URL requesting signing.</param>
/// <param name="Url">The URL to be signed.</param>
/// <param name="Method">The HTTP method (GET, POST, etc.).</param>
/// <param name="Headers">The HTTP headers to be signed.</param>
/// <param name="TabId">The browser tab ID where the request originated.</param>
/// <param name="TabUrl">The full URL of the requesting tab.</param>
/// <param name="OriginalRequestId">The original requestId from ContentScript→BW message, needed to route response back to ContentScript.</param>
/// <param name="OriginalType">The original message type from ContentScript (e.g., "/signify/sign-request").</param>
/// <param name="OriginalPayload">The original payload from ContentScript, if needed by App UI.</param>
/// <param name="RememberedPrefix">The remembered identifier prefix for this origin, if any.</param>
public record RequestSignHeadersPayload(
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("headers")] Dictionary<string, string> Headers,
    [property: JsonPropertyName("tabId")] int TabId,
    [property: JsonPropertyName("tabUrl")] string? TabUrl = null,
    [property: JsonPropertyName("originalRequestId")] string? OriginalRequestId = null,
    [property: JsonPropertyName("originalType")] string? OriginalType = null,
    [property: JsonPropertyName("originalPayload")] object? OriginalPayload = null,
    [property: JsonPropertyName("rememberedPrefix")] string? RememberedPrefix = null
);

/// <summary>
/// Request from BackgroundWorker to App to show SignHeaders UI.
/// User approves or rejects signing the HTTP request headers.
/// </summary>
public record BwAppRequestSignHeadersMessage : BwAppMessage<RequestSignHeadersPayload> {
    public BwAppRequestSignHeadersMessage(string requestId, RequestSignHeadersPayload payload)
        : base(BwAppMessageType.RequestSignHeaders, requestId, payload) { }
}
