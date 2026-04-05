namespace Extension.Services.Storage;

/// <summary>
/// Observer that receives one callback per Chrome storage.onChanged batch event.
///
/// Unlike the per-record <see cref="IObserver{T}"/> used with <see cref="IStorageGateway.Subscribe{T}"/>,
/// a batch observer sees all records changed in a single Chrome event together, so subscribers can
/// coalesce multi-key writes into a single UI update cycle.
///
/// Note: batch observers receive NO initial value push on Subscribe. Callers that need initial state
/// must call <see cref="IStorageGateway.GetItems"/> explicitly after subscribing.
/// </summary>
public interface IStorageBatchObserver {
    /// <summary>
    /// Called once per chrome.storage.onChanged event for the subscribed area, with all records
    /// that changed in that event.
    /// </summary>
    /// <param name="area">The storage area the batch originated from.</param>
    /// <param name="batch">The set of record changes in this batch.</param>
    void OnBatch(StorageArea area, StorageChangeBatch batch);
}
