using Extension.Models.Storage;

namespace Extension.Models {
    /// <summary>
    /// Session storage wrapper for credentials fetched from KERIA.
    /// Stores the raw JSON string from signify-ts to preserve CESR/SAID field ordering.
    /// BackgroundWorker writes proactively; App components read directly from session storage.
    /// </summary>
    public record CachedCredentials(string RawJson) : IStorageModel;
}
