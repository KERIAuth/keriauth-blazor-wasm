using System.Text.Json.Serialization;

namespace Extension.Models.Messages; 


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


// Updated ApprovedSignRequest
public record PortApprovedSignRequest {
    [JsonPropertyName("originStr")]
    public string OriginStr { get; init; }

    [JsonPropertyName("url")]
    public string Url { get; init; }

    [JsonPropertyName("method")]
    public string Method { get; init; }

    [JsonPropertyName("initHeadersDict")]
    public Dictionary<string, string>? InitHeadersDict { get; init; }

    [JsonPropertyName("selectedPrefix")]
    public string SelectedPrefix { get; init; }

    [JsonConstructor]
    public PortApprovedSignRequest(
        string originStr,
        string url,
        string method,
        string selectedPrefix,
        Dictionary<string, string>? initHeadersDict = null) {
        OriginStr = originStr;
        Url = url;
        Method = method;
        SelectedPrefix = selectedPrefix;
        InitHeadersDict = initHeadersDict;
    }
}
