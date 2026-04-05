namespace Extension.Services.Storage;

using System.Text.Json;
using Extension.Helper;
using Extension.Models.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// A set of storage record changes that arrived together in a single
/// chrome.storage.onChanged event. Delivered to <see cref="IStorageBatchObserver.OnBatch"/>.
///
/// A batch may contain any mixture of additions, updates, and deletions.
/// A deletion is represented by a key whose change entry has an old value but no new value
/// (Chrome emits these when .remove() or .clear() is called).
///
/// Values are held as raw <see cref="JsonElement"/> and deserialized on demand per type,
/// so a subscriber that only cares about one record in a multi-key batch does not pay
/// deserialization cost for every key.
/// </summary>
public sealed class StorageChangeBatch {
    // key = typeof(T).Name, value = (newValue, oldValue). Either or both sides may be null.
    // Both null is not stored — such entries are skipped during construction.
    private readonly IReadOnlyDictionary<string, (JsonElement? NewValue, JsonElement? OldValue)> _changes;
    private readonly ILogger? _logger;

    // Memoization caches for deserialized values, keyed by typeof(T).Name.
    // A key present in either map means GetNew<T>() / GetOld<T>() was already called for that type.
    private readonly Dictionary<string, object?> _newCache = new();
    private readonly Dictionary<string, object?> _oldCache = new();

    internal StorageChangeBatch(
        IReadOnlyDictionary<string, (JsonElement? NewValue, JsonElement? OldValue)> changes,
        ILogger? logger = null) {
        _changes = changes;
        _logger = logger;
    }

    /// <summary>
    /// Storage keys (typeof(T).Name form) of every record that changed in this batch.
    /// Useful for logging and for iterating over all changes without knowing types in advance.
    /// </summary>
    public IReadOnlyCollection<string> ChangedKeys => (IReadOnlyCollection<string>)_changes.Keys;

    /// <summary>
    /// Returns true if the batch contains a change for the given record type
    /// (addition, update, or deletion).
    /// </summary>
    public bool Contains<T>() where T : class, IStorageModel =>
        _changes.ContainsKey(typeof(T).Name);

    /// <summary>
    /// Returns true if the given record was deleted in this batch (old value present,
    /// new value absent). Emitted by Chrome for .remove() and .clear() operations.
    /// </summary>
    public bool IsDeletion<T>() where T : class, IStorageModel {
        if (!_changes.TryGetValue(typeof(T).Name, out var change)) return false;
        return change.NewValue is null && change.OldValue is not null;
    }

    /// <summary>
    /// The new value of the record after this batch, or null if the record was deleted,
    /// not present in this batch, or could not be deserialized.
    /// </summary>
    public T? GetNew<T>() where T : class, IStorageModel =>
        Deserialize<T>(isNew: true);

    /// <summary>
    /// The old value of the record before this batch, or null if the record was newly
    /// added (no prior value), not present in this batch, or could not be deserialized.
    /// </summary>
    public T? GetOld<T>() where T : class, IStorageModel =>
        Deserialize<T>(isNew: false);

    private T? Deserialize<T>(bool isNew) where T : class, IStorageModel {
        var key = typeof(T).Name;
        var cache = isNew ? _newCache : _oldCache;

        if (cache.TryGetValue(key, out var cached)) {
            return cached as T;
        }

        if (!_changes.TryGetValue(key, out var change)) {
            cache[key] = null;
            return null;
        }

        var element = isNew ? change.NewValue : change.OldValue;
        if (element is null) {
            cache[key] = null;
            return null;
        }

        try {
            var value = JsonSerializer.Deserialize<T>(element.Value, JsonOptions.Storage);
            cache[key] = value;
            return value;
        }
        catch (Exception ex) {
            _logger?.LogError(ex, nameof(StorageChangeBatch) + "." + nameof(Deserialize) + ": Failed to deserialize {Key} ({Side})",
                key, isNew ? "new" : "old");
            cache[key] = null;
            return null;
        }
    }
}
