namespace Extension.Models.Storage;

using System.Text.Json.Serialization;

/// <summary>
/// Session storage record containing a monotonic sequence number incremented by
/// BackgroundWorker on every session storage write. AppCache uses this to determine
/// whether it has processed the latest BW writes (WaitForStorageSync).
/// </summary>
public record SessionSequence : IStorageModel {
    [JsonPropertyName("Seq")]
    public long Seq { get; init; }
}
