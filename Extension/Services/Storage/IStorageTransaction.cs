namespace Extension.Services.Storage;

using Extension.Models.Storage;

/// <summary>
/// Builder for a multi-record atomic write. All <see cref="SetItem{T}"/> calls made inside
/// <see cref="IStorageGateway.WriteTransaction"/> are collected and issued as a single
/// chrome.storage.*.set(dict) call, producing a single onChanged event.
/// </summary>
public interface IStorageTransaction {
    /// <summary>
    /// Stage a record for the transaction. Storage key is derived from typeof(T).Name,
    /// matching the convention used by all other storage APIs in this project.
    /// </summary>
    /// <typeparam name="T">Record type (determines the storage key).</typeparam>
    /// <param name="value">Value to store.</param>
    void SetItem<T>(T value) where T : class, IStorageModel;
}
