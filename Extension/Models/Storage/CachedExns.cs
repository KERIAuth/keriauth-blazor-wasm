namespace Extension.Models.Storage;

using System.Text.Json.Serialization;

/// <summary>
/// Caches exchange (exn) results (as raw JSON) keyed by SAID.
/// Avoids repeated GetExchange network calls during notification processing,
/// credential grant flows, and schema resolution.
///
/// Storage key: "CachedExns"
/// Storage area: Session
/// Lifetime: Cleared on session lock/expiration (via ClearKeriaSessionRecordsAsync)
///           and on KERIA config change (via ClearSessionForConfigChangeAsync).
/// </summary>
public record CachedExns : IStorageModel {
    [JsonPropertyName("exchanges")]
    public Dictionary<string, string> Exchanges { get; init; } = [];
}
