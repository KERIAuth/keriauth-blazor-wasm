using System.Text.Json.Serialization;
using Extension.Models.Storage;

namespace Extension.Models;

/// <summary>
/// Session storage for credentials fetched from KERIA.
/// Each credential is stored individually, keyed by its sad.d (SAID) value.
/// Values are raw JSON strings to preserve CESR/SAID field ordering.
/// </summary>
public record CachedCredentials : IStorageModel {
    [JsonPropertyName("Credentials")]
    public Dictionary<string, string> Credentials { get; init; } = new();
}
