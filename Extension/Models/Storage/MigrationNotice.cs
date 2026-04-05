namespace Extension.Models.Storage;

using System.Text.Json.Serialization;

/// <summary>
/// Local storage record indicating that one or more Local storage records were
/// discarded due to schema version mismatches (upgrade from a prior incompatible version).
/// Written by StorageService.GetItem&lt;T&gt;() when a versioned record fails the version check.
/// Read by the App on startup to warn the user that their prior configuration was reset.
/// Cleared after the user acknowledges.
///
/// NOTE: Intentionally not an IVersionedStorageModel — it has no SchemaVersion and is
/// not in StorageModelRegistry. This keeps it safe from the version-check-discard loop
/// that it itself reports on. Its schema should remain trivial to avoid ever needing versioning.
/// </summary>
public record MigrationNotice : IStorageModel {
    /// <summary>
    /// Names of record types whose stored versions did not match the expected version.
    /// </summary>
    [JsonPropertyName("discardedTypeNames")]
    public List<string> DiscardedTypeNames { get; init; } = [];
}
