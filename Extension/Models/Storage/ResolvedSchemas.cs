namespace Extension.Models.Storage;

using System.Text.Json.Serialization;

/// <summary>
/// Caches the set of schema SAIDs confirmed to be resolved in KERIA.
/// Avoids repeated GetSchema network calls to KERIA for schemas already verified.
///
/// Storage key: "ResolvedSchemas"
/// Storage area: Session
/// Lifetime: Cleared on session lock/expiration (via ClearKeriaSessionRecordsAsync)
///           and on KERIA config change (via ClearSessionForConfigChangeAsync).
/// </summary>
public record ResolvedSchemas : IStorageModel {
    [JsonPropertyName("saids")]
    public HashSet<string> Saids { get; init; } = [];
}
