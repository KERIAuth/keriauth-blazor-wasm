namespace Extension.Models.Storage;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Represents a single pending request from BackgroundWorker to App.
/// Direction: BackgroundWorker → App
/// These requests are stored in session storage and picked up by App (Popup/SidePanel/Tab)
/// when it subscribes to storage changes.
/// </summary>
public record PendingBwAppRequest {
    /// <summary>
    /// Unique identifier for this request.
    /// Used to correlate response back to the waiting BackgroundWorker.
    /// </summary>
    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    /// <summary>
    /// The message type (e.g., "/KeriAuth/BwApp/signRequest").
    /// App uses this to determine how to handle the request.
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    /// The request payload as JSON-serializable object.
    /// App deserializes this based on the Type.
    /// </summary>
    [JsonPropertyName("payload")]
    public object? Payload { get; init; }

    /// <summary>
    /// UTC timestamp when this request was created.
    /// Can be used for timeout/cleanup of stale requests.
    /// </summary>
    [JsonPropertyName("createdAtUtc")]
    public required DateTime CreatedAtUtc { get; init; }

    /// <summary>
    /// Optional tab ID that originated or is associated with this request.
    /// Useful for context when App needs to know which tab triggered the request.
    /// </summary>
    [JsonPropertyName("tabId")]
    public int? TabId { get; init; }

    /// <summary>
    /// Optional URL associated with the request origin.
    /// </summary>
    [JsonPropertyName("tabUrl")]
    public string? TabUrl { get; init; }

    /// <summary>
    /// Port ID for routing the response via port-based messaging.
    /// When set, response should be sent via BwPortService.SendRpcResponseAsync instead of sendMessage.
    /// </summary>
    [JsonPropertyName("portId")]
    public string? PortId { get; init; }

    /// <summary>
    /// PortSession ID for the response routing.
    /// </summary>
    [JsonPropertyName("portSessionId")]
    public string? PortSessionId { get; init; }

    /// <summary>
    /// RPC request ID for correlating the response.
    /// Different from RequestId when the original CS request had its own requestId.
    /// </summary>
    [JsonPropertyName("rpcRequestId")]
    public string? RpcRequestId { get; init; }

    /// <summary>
    /// Gets the payload deserialized to the specified type.
    /// Handles both direct type match and JsonElement deserialization (after storage round-trip).
    /// </summary>
    /// <typeparam name="T">The expected payload type.</typeparam>
    /// <returns>The deserialized payload, or null if deserialization fails.</returns>
    public T? GetPayload<T>() where T : class {
        if (Payload is T typedPayload)
            return typedPayload;

        if (Payload is System.Text.Json.JsonElement jsonElement)
            return jsonElement.Deserialize<T>();

        return null;
    }
}

/// <summary>
/// Collection of pending BW→App requests stored in session storage.
/// Supports multiple concurrent requests (e.g., version update + signRequest).
///
/// Storage key: "PendingBwAppRequests"
/// Storage area: Session
/// Lifetime: Cleared on browser close, persists across service worker restarts
///
/// Design notes:
/// - BackgroundWorker adds requests to this collection
/// - App (Popup/SidePanel/Tab) subscribes to changes and processes requests
/// - Requests are removed after App sends response or on timeout
/// - Initial implementation may log error if queue depth > 1 (future: queue processing)
/// </summary>
public record PendingBwAppRequests : IStorageModel {
    /// <summary>
    /// List of pending requests awaiting processing by App.
    /// Ordered by CreatedAtUtc (oldest first).
    /// </summary>
    [JsonPropertyName("requests")]
    public required IReadOnlyList<PendingBwAppRequest> Requests { get; init; } = [];

    /// <summary>
    /// Creates an empty pending requests collection.
    /// </summary>
    public static PendingBwAppRequests Empty => new() { Requests = [] };

    /// <summary>
    /// Returns true if there are no pending requests.
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty => Requests.Count == 0;

    /// <summary>
    /// Returns the count of pending requests.
    /// </summary>
    [JsonIgnore]
    public int Count => Requests.Count;

    /// <summary>
    /// Gets the oldest pending request (first in queue), or null if empty.
    /// </summary>
    [JsonIgnore]
    public PendingBwAppRequest? NextRequest => Requests.Count > 0 ? Requests[0] : null;

    /// <summary>
    /// Creates a new collection with the specified request added.
    /// New requests are added at the end (FIFO queue).
    /// </summary>
    public PendingBwAppRequests WithRequest(PendingBwAppRequest request) {
        var newList = new List<PendingBwAppRequest>(Requests) { request };
        return this with { Requests = newList };
    }

    /// <summary>
    /// Creates a new collection with the specified request removed.
    /// </summary>
    public PendingBwAppRequests WithoutRequest(string requestId) {
        var newList = Requests.Where(r => r.RequestId != requestId).ToList();
        return this with { Requests = newList };
    }

    /// <summary>
    /// Creates a new collection with requests older than the specified age removed.
    /// Useful for cleaning up stale requests.
    /// </summary>
    public PendingBwAppRequests WithoutStaleRequests(TimeSpan maxAge) {
        var cutoff = DateTime.UtcNow - maxAge;
        var newList = Requests.Where(r => r.CreatedAtUtc > cutoff).ToList();
        return this with { Requests = newList };
    }
}
