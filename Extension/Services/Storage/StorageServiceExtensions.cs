namespace Extension.Services.Storage;

using Extension.Models.Storage;
using Microsoft.Extensions.Logging;

public static class StorageServiceExtensions {
    /// <summary>
    /// Creates and subscribes a StorageObserver for the specified type and storage area.
    /// Returns a disposable observer - dispose to unsubscribe.
    /// </summary>
    /// <typeparam name="T">Storage model type (must implement IStorageModel)</typeparam>
    /// <param name="storageService">Storage service instance</param>
    /// <param name="storageArea">Storage area to monitor</param>
    /// <param name="onNext">Callback when value changes</param>
    /// <param name="onError">Optional error handler</param>
    /// <param name="onCompleted">Optional completion handler</param>
    /// <param name="logger">Optional _logger</param>
    /// <returns>Disposable observer subscription</returns>
    public static StorageObserver<T> CreateObserver<T>(
        this IStorageService storageService,
        StorageArea storageArea,
        Action<T> onNext,
        Action<Exception>? onError = null,
        Action? onCompleted = null,
        ILogger? logger = null
    ) where T : class, IStorageModel {
        return new StorageObserver<T>(
            storageService,
            storageArea,
            onNext,
            onError,
            onCompleted,
            logger
        );
    }
}
