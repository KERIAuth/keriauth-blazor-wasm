namespace Extension.Services.Storage;

using System.Text.Json;
using Extension.Helper;
using Extension.Models.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// Result of a bulk read issued via <see cref="IStorageGateway.GetItems"/>. Holds raw
/// values for every requested record type and deserializes on demand so callers only
/// pay deserialization cost for records they actually read.
///
/// Version-mismatch handling: a single mismatched record does NOT fail the whole batch.
/// During construction, the schema version of each <see cref="IVersionedStorageModel"/>
/// is peeked (without full deserialization) against <see cref="StorageModelRegistry"/>.
/// Mismatched keys are recorded in <see cref="VersionMismatchKeys"/>, and
/// <see cref="Get{T}"/> returns null for them — matching the existing per-record
/// <see cref="IStorageGateway.GetItem{T}"/> discard-and-default semantics.
/// </summary>
public sealed class StorageReadResult {
    // Raw JsonElement for each requested type (by typeof(T).Name), or null if the
    // key was not present in the underlying storage area at the time of the read.
    private readonly IReadOnlyDictionary<string, JsonElement?> _rawByKey;
    private readonly HashSet<string> _versionMismatchKeys;
    private readonly ILogger? _logger;

    // Memoization cache for deserialized values. Single-threaded use expected
    // (startup fetch path). A key present here means Get<T>() was already called.
    private readonly Dictionary<string, object?> _deserializedCache = new();

    /// <summary>
    /// Create a StorageReadResult from raw values. Internal constructor — normally
    /// built inside <see cref="StorageGateway.GetItems"/>, and exposed to Extension.Tests
    /// via InternalsVisibleTo for unit-testing without a browser environment.
    /// </summary>
    /// <param name="rawByKey">Map of typeof(T).Name → raw JsonElement or null if absent.</param>
    /// <param name="versionMismatchKeys">Keys that were present but had a schema version mismatch.</param>
    /// <param name="logger">Optional logger for diagnostic warnings.</param>
    internal StorageReadResult(
        IReadOnlyDictionary<string, JsonElement?> rawByKey,
        HashSet<string> versionMismatchKeys,
        ILogger? logger = null) {
        _rawByKey = rawByKey;
        _versionMismatchKeys = versionMismatchKeys;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves a typed value from the bulk read. Returns null if the record was not
    /// present in the underlying storage area at the time of the read, or if its stored
    /// schema version did not match the expected version registered in
    /// <see cref="StorageModelRegistry"/>.
    ///
    /// Deserializes on first access per type; subsequent calls return the cached value.
    /// </summary>
    public T? Get<T>() where T : class, IStorageModel {
        var key = typeof(T).Name;

        if (_deserializedCache.TryGetValue(key, out var cached)) {
            return cached as T;
        }

        if (_versionMismatchKeys.Contains(key)) {
            _deserializedCache[key] = null;
            return null;
        }

        if (!_rawByKey.TryGetValue(key, out var element) || element is null) {
            _deserializedCache[key] = null;
            return null;
        }

        try {
            var value = JsonSerializer.Deserialize<T>(element.Value, JsonOptions.Storage);
            _deserializedCache[key] = value;
            return value;
        }
        catch (Exception ex) {
            _logger?.LogError(ex, nameof(StorageReadResult) + "." + nameof(Get) + ": Failed to deserialize {Key}", key);
            _deserializedCache[key] = null;
            return null;
        }
    }

    /// <summary>
    /// Returns true if the record of type T was present in the underlying storage but
    /// its stored schema version did not match the expected version. Useful for
    /// distinguishing "never stored" from "stored but discarded due to upgrade".
    /// </summary>
    public bool IsVersionMismatch<T>() where T : class, IVersionedStorageModel =>
        _versionMismatchKeys.Contains(typeof(T).Name);

    /// <summary>
    /// Keys (typeof(T).Name) of every record that was present but discarded due to
    /// schema-version mismatch during this bulk read.
    /// </summary>
    public IReadOnlyCollection<string> VersionMismatchKeys => _versionMismatchKeys;
}
