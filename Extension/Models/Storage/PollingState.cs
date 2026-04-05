namespace Extension.Models.Storage;

using System.Text.Json.Serialization;

/// <summary>
/// Session storage record tracking when KERIA-fetched resources were last retrieved.
/// Used by polling services to skip redundant fetches within a configurable threshold.
/// </summary>
public record PollingState : IStorageModel {
    [JsonPropertyName("ConnectionsLastFetchedUtc")]
    public DateTime? ConnectionsLastFetchedUtc { get; init; }

    [JsonPropertyName("IdentifiersLastFetchedUtc")]
    public DateTime? IdentifiersLastFetchedUtc { get; init; }

    [JsonPropertyName("CredentialsLastFetchedUtc")]
    public DateTime? CredentialsLastFetchedUtc { get; init; }
}
