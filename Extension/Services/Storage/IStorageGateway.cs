namespace Extension.Services.Storage;

using Extension.Models.Storage;
using FluentResults;

/// <summary>
/// Status of a versioned storage record read.
/// </summary>
public enum StorageItemStatus {
    Found,
    NotFound,
    VersionMismatch
}

/// <summary>
/// Batched-capable storage gateway over chrome.storage.*. Introduced alongside
/// <see cref="IStorageGateway"/> to add bulk read, bulk write, write transactions,
/// and batched change notifications that the existing service does not expose
/// at its public API despite WebExtensions.Net already supporting them underneath.
///
/// Migration strategy: this interface coexists with <see cref="IStorageGateway"/>.
/// Call sites migrate incrementally; once all producers and observers are on
/// <see cref="IStorageGateway"/>, the old service is deleted in a later phase.
///
/// All storage keys are derived from typeof(T).Name, matching the convention used
/// by <see cref="IStorageGateway"/>, so records written by one service are readable
/// by the other during migration.
/// </summary>
public interface IStorageGateway {
    /// <summary>
    /// Get a single record from the given storage area.
    /// Same semantics as <see cref="IStorageGateway.GetItem{T}"/>: returns null when the
    /// record is absent or when its stored schema version does not match the expected
    /// version (discard-and-default).
    /// </summary>
    Task<Result<T?>> GetItem<T>(StorageArea area = StorageArea.Local)
        where T : class, IStorageModel;

    /// <summary>
    /// Write a single record to the given storage area.
    /// </summary>
    Task<Result> SetItem<T>(T value, StorageArea area = StorageArea.Local)
        where T : class, IStorageModel;

    /// <summary>
    /// Remove a single record from the given storage area.
    /// </summary>
    Task<Result> RemoveItem<T>(StorageArea area = StorageArea.Local)
        where T : class, IStorageModel;

    /// <summary>
    /// Clear all items in the specified storage area.
    /// </summary>
    Task<Result> Clear(StorageArea area = StorageArea.Local);

    /// <summary>
    /// Probes a versioned record and reports its status (Found, NotFound, VersionMismatch).
    /// Used during startup migration detection.
    /// </summary>
    Task<Result<StorageItemStatus>> GetItemStatus<T>(StorageArea area = StorageArea.Local)
        where T : class, IVersionedStorageModel;

    /// <summary>
    /// Bulk read of multiple record types in a single chrome.storage.*.get(keys[])
    /// round-trip. Returns a <see cref="StorageReadResult"/> that deserializes on demand.
    ///
    /// A single record with a schema-version mismatch does NOT fail the bulk read —
    /// it is tracked on the result and returned as null from <see cref="StorageReadResult.Get{T}"/>.
    /// </summary>
    Task<Result<StorageReadResult>> GetItems(StorageArea area, params Type[] types);

    /// <summary>
    /// Bulk write of multiple record values in a single chrome.storage.*.set(dict) call.
    /// Delivered as a single onChanged event to batch observers.
    /// </summary>
    Task<Result> SetItems(StorageArea area, params IStorageModel[] values);

    /// <summary>
    /// Ergonomic builder form of <see cref="SetItems"/>. Collects all .Set() calls made
    /// inside the builder action and issues them as a single atomic write.
    /// </summary>
    Task<Result> WriteTransaction(StorageArea area, Action<IStorageTransaction> build);

    /// <summary>
    /// Subscribe to changes of a single record type. Semantics match
    /// <see cref="IStorageGateway.Subscribe{T}"/>, including the async initial-value push
    /// after subscription.
    /// </summary>
    IDisposable Subscribe<T>(IObserver<T> observer, StorageArea area = StorageArea.Local)
        where T : class, IStorageModel;

    /// <summary>
    /// Subscribe to batched change notifications for an area. The observer receives one
    /// callback per chrome.storage.onChanged event for the subscribed area, with every
    /// record that changed in that event delivered together in one
    /// <see cref="StorageChangeBatch"/>.
    ///
    /// No initial value is pushed on subscribe — callers needing initial state should
    /// call <see cref="GetItems"/> explicitly after subscribing.
    /// </summary>
    IDisposable SubscribeBatch(IStorageBatchObserver observer, StorageArea area);
}
