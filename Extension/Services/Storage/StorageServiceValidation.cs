namespace Extension.Services.Storage;

using FluentResults;
using Extension.Models;

/// <summary>
/// Validation helper for storage operations across different storage areas.
/// Enforces rules like read-only Managed storage and quota-only Local/Sync storage.
/// </summary>
internal static class StorageServiceValidation {
    /// <summary>
    /// Operations not allowed on Managed storage (read-only for extensions).
    /// IT administrators configure Managed storage via enterprise policies.
    /// </summary>
    private static readonly HashSet<string> ManagedReadOnlyOps = new() {
        nameof(IStorageService.Clear),
        nameof(IStorageService.RemoveItem),
        nameof(IStorageService.SetItem),
        nameof(IStorageService.RestoreBackupItems)
    };

    /// <summary>
    /// Operations not allowed on Local/Managed storage (no quota tracking in WebExtensions.Net).
    /// NOTE: WebExtensions.Net defines Session and Sync as StorageAreaWithUsage (with GetBytesInUse).
    /// This differs from Chrome docs which say Local/Sync have quotas, but we follow the library's API.
    /// </summary>
    private static readonly HashSet<string> QuotaRequiredOps = new() {
        nameof(IStorageService.GetBytesInUse),
        nameof(IStorageService.GetQuota)
    };

    /// <summary>
    /// Validate that an operation is allowed on the specified storage area.
    /// </summary>
    /// <param name="operation">Operation name (e.g., "SetItem", "GetQuota")</param>
    /// <param name="area">Storage area to validate against</param>
    /// <returns>Result.Ok() if valid, Result.Fail() with error message if invalid</returns>
    public static Result ValidateOperation(string operation, StorageArea area) {
        // Managed storage is read-only for extensions
        if (area == StorageArea.Managed && ManagedReadOnlyOps.Contains(operation)) {
            return Result.Fail(new StorageError(
                $"{operation} not allowed on Managed storage area (read-only for extensions). " +
                "Managed storage is configured by IT administrators via enterprise policies."
            ));
        }

        // Local and Managed storage have no quota tracking in WebExtensions.Net
        // (Session and Sync are StorageAreaWithUsage with GetBytesInUse)
        if ((area == StorageArea.Local || area == StorageArea.Managed)
            && QuotaRequiredOps.Contains(operation)) {
            return Result.Fail(new StorageError(
                $"{operation} not available for {area} storage (no quota API in WebExtensions.Net). " +
                "Only Session and Sync storage areas have GetBytesInUse() method."
            ));
        }

        return Result.Ok();
    }

    /// <summary>
    /// Validate operation and return typed failure result if invalid.
    /// Convenience method for operations returning Result&lt;T&gt;.
    /// </summary>
    /// <typeparam name="T">Type parameter for Result</typeparam>
    /// <param name="operation">Operation name</param>
    /// <param name="area">Storage area</param>
    /// <returns>Failed Result&lt;T&gt; if validation fails</returns>
    /// <exception cref="InvalidOperationException">If validation succeeds (use ValidateOperation instead)</exception>
    public static Result<T> ValidateAndFail<T>(string operation, StorageArea area) {
        var validation = ValidateOperation(operation, area);
        return validation.IsFailed
            ? Result.Fail<T>(validation.Errors)
            : throw new InvalidOperationException(
                "ValidateAndFail should only be called when expecting failure. " +
                "Use ValidateOperation for non-generic Result."
            );
    }
}
