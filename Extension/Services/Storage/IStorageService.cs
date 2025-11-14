namespace Extension.Services.Storage;

using FluentResults;

/// <summary>
/// Unified storage service interface supporting all chrome.storage areas.
/// All operations are type-safe using record models (no string keys).
/// Storage keys are derived from typeof(T).Name.
/// </summary>
public interface IStorageService {


    /// <summary>
    /// Clear all items in specified storage area.
    /// NOTE: Not valid for StorageArea.Managed (read-only).
    /// </summary>
    /// <param name="area">Storage area to clear (default: Local)</param>
    Task<Result> Clear(StorageArea area = StorageArea.Local);

    /// <summary>
    /// Remove item by type name from specified storage area.
    /// Storage key is derived from typeof(T).Name.
    /// NOTE: Not valid for StorageArea.Managed (read-only).
    /// </summary>
    /// <typeparam name="T">Type of item to remove (determines storage key)</typeparam>
    /// <param name="area">Storage area (default: Local)</param>
    Task<Result> RemoveItem<T>(StorageArea area = StorageArea.Local);

    /// <summary>
    /// Get item by type name from specified storage area.
    /// Storage key is derived from typeof(T).Name.
    /// </summary>
    /// <typeparam name="T">Type of item to retrieve</typeparam>
    /// <param name="area">Storage area (default: Local)</param>
    /// <returns>Result containing the item if found, or null if not found</returns>
    Task<Result<T?>> GetItem<T>(StorageArea area = StorageArea.Local);

    /// <summary>
    /// Set item using type name as key in specified storage area.
    /// Storage key is derived from typeof(T).Name.
    /// NOTE: Not valid for StorageArea.Managed (read-only).
    /// </summary>
    /// <typeparam name="T">Type of item to store (determines storage key)</typeparam>
    /// <param name="value">Value to store</param>
    /// <param name="area">Storage area (default: Local)</param>
    Task<Result> SetItem<T>(T value, StorageArea area = StorageArea.Local);

    /// <summary>
    /// Get backup of all items in specified storage area as JSON string.
    /// Useful for export/restore functionality.
    /// </summary>
    /// <param name="area">Storage area to backup (default: Local)</param>
    /// <param name="excludeKeys">Keys to exclude from backup (e.g., transient state)</param>
    Task<Result<string>> GetBackupItems(
        StorageArea area = StorageArea.Local,
        List<string>? excludeKeys = null
    );

    /// <summary>
    /// Restore items from backup JSON string to specified storage area.
    /// NOTE: Not valid for StorageArea.Managed (read-only).
    /// </summary>
    /// <param name="backupJson">JSON string from GetBackupItems()</param>
    /// <param name="area">Storage area to restore to (default: Local)</param>
    Task<Result> RestoreBackupItems(
        string backupJson,
        StorageArea area = StorageArea.Local
    );

    /// <summary>
    /// Get bytes used in specified storage area.
    /// Only valid for StorageArea.Local and StorageArea.Sync (have quotas).
    /// Returns error for Session/Managed (no quota tracking).
    /// </summary>
    /// <param name="area">Storage area (default: Local)</param>
    Task<Result<long>> GetBytesInUse(StorageArea area = StorageArea.Local);

    /// <summary>
    /// Get bytes used by a specific type in specified storage area.
    /// Only valid for StorageArea.Local and StorageArea.Sync.
    /// </summary>
    /// <typeparam name="T">Type to check storage usage for</typeparam>
    /// <param name="area">Storage area (default: Local)</param>
    Task<Result<long>> GetBytesInUse<T>(StorageArea area = StorageArea.Local);

    /// <summary>
    /// Get quota information for specified storage area.
    /// Only valid for StorageArea.Local and StorageArea.Sync.
    /// - Local: Typically 10MB (10,485,760 bytes)
    /// - Sync: 102,400 bytes (100KB) total, 8,192 bytes per item
    /// </summary>
    /// <param name="area">Storage area (default: Local)</param>
    Task<Result<StorageQuota>> GetQuota(StorageArea area = StorageArea.Local);

    /// <summary>
    /// Subscribe to storage changes for a specific type in specified storage area.
    /// Call Initialize(area) first to enable change notifications.
    /// Works for ALL storage areas including Managed (IT policies can change).
    /// </summary>
    /// <typeparam name="T">Type to monitor for changes (e.g., Preferences, PasscodeModel)</typeparam>
    /// <param name="observer">Observer to notify of changes</param>
    /// <param name="area">Storage area to monitor (default: Local)</param>
    /// <returns>Disposable subscription - dispose to unsubscribe</returns>
    IDisposable Subscribe<T>(
        IObserver<T> observer,
        StorageArea area = StorageArea.Local
    );
}

/// <summary>
/// Storage quota information for areas with quota limits (Local and Sync).
/// </summary>
public record StorageQuota {
    /// <summary>Total bytes available in this storage area</summary>
    public required long QuotaBytes { get; init; }

    /// <summary>Bytes currently in use</summary>
    public required long UsedBytes { get; init; }

    /// <summary>Bytes remaining</summary>
    public long RemainingBytes => QuotaBytes - UsedBytes;

    /// <summary>Percentage used (0-100)</summary>
    public double PercentUsed => QuotaBytes > 0 ? (UsedBytes * 100.0 / QuotaBytes) : 0;

    /// <summary>Max bytes per item (only for Sync storage)</summary>
    public long? MaxBytesPerItem { get; init; }

    /// <summary>Max number of items (only for Sync storage)</summary>
    public int? MaxItems { get; init; }
}
