namespace Extension.Models.Storage;

using System.Text.Json.Serialization;

/// <summary>
/// Tracks step-level progress of PrimeDataService operations in session storage.
/// Written by PrimeDataService (BackgroundWorker), observed by PrimeDataPage (App).
/// Storage key: "PrimeDataProgress" (derived from type name)
/// Storage area: Session
/// </summary>
public record PrimeDataProgress : IStorageModel {
    [JsonPropertyName("step")]
    public int Step { get; init; }

    [JsonPropertyName("totalSteps")]
    public int TotalSteps { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("isComplete")]
    public bool IsComplete { get; init; }

    [JsonPropertyName("isError")]
    public bool IsError { get; init; }
}
