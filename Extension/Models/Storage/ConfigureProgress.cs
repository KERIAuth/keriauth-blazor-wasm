namespace Extension.Models.Storage;

using System.Text.Json.Serialization;

/// <summary>
/// Tracks step-level progress of ConfigureService operations in session storage.
/// Written by ConfigureService (BackgroundWorker), observed by ConfigurePage (App).
/// Storage key: "ConfigureProgress" (derived from type name)
/// Storage area: Session
/// </summary>
public record ConfigureProgress : IStorageModel {
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
