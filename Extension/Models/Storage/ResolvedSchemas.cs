namespace Extension.Models.Storage;

using System.Text.Json.Serialization;

/// <summary>
/// Caches schema bodies (as raw JSON) keyed by SAID, for schemas confirmed resolved in KERIA.
/// Avoids repeated GetSchema network calls and provides schema field descriptions
/// for UI label rendering (e.g., IPEX apply messages that reference a schema SAID).
///
/// Storage key: "ResolvedSchemas"
/// Storage area: Session
/// Lifetime: Cleared on session lock/expiration (via ClearKeriaSessionRecordsAsync)
///           and on KERIA config change (via ClearSessionForConfigChangeAsync).
/// </summary>
public record ResolvedSchemas : IStorageModel {
    [JsonPropertyName("schemas")]
    public Dictionary<string, string> Schemas { get; init; } = [];
}
