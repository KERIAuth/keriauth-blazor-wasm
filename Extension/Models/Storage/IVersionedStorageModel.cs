namespace Extension.Models.Storage;

/// <summary>
/// Interface for Local storage models that support schema versioning.
/// Only Local storage records implement this — Session records are ephemeral
/// and do not need versioning.
/// On schema version mismatch, GetItem returns default (null), and callers
/// re-initialize via existing default-creation logic.
/// </summary>
public interface IVersionedStorageModel : IStorageModel {
    int SchemaVersion { get; init; }
}
