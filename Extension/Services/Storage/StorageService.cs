namespace Extension.Services.Storage;

using System.Text;
using System.Text.Json;
using Extension.Helper;
using Extension.Models;
using FluentResults;
using JsBind.Net;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using WebExtensions.Net;

/// <summary>
/// Unified storage service supporting all chrome.storage areas (Local, Session, Sync, Managed).
/// Uses WebExtensions.Net native event handling - no JavaScript helpers required.
/// </summary>
public class StorageService : IStorageService, IDisposable {
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<StorageService> _logger;
    private readonly WebExtensionsApi _webExtensionsApi;

    // Track which areas have been initialized for change notifications
    private readonly HashSet<StorageArea> _initializedAreas = new();

    // Observer storage
    // Outer key: StorageArea (e.g., Local, Session)
    // Inner key: Type name (e.g., "Preferences", "PasscodeModel")
    // Inner value: List of observer entries
    private readonly Dictionary<StorageArea, Dictionary<string, List<ObserverEntry>>> _observersByArea = new();

    // Helper record to store observer with its typed notification callback
    private sealed record ObserverEntry(object Observer, Type ElementType, Action<object> NotifyCallback);

    // Single global callback for ALL storage areas (chrome.storage.onChanged fires once for all areas)
    private Action<object, string>? _globalCallback;
    private bool _listenerRegistered;

    public static LogLevel ServiceLogLevel { get; set; } = LogLevel.Debug;

    public StorageService(
        IJSRuntime jsRuntime,
        IJsRuntimeAdapter jsRuntimeAdapter,
        ILogger<StorageService> logger
    ) {
        _jsRuntime = jsRuntime;
        _logger = logger;
        _webExtensionsApi = new WebExtensionsApi(jsRuntimeAdapter);
        _logger.Log(ServiceLogLevel, nameof(StorageService) + ": constructor");
        Initialize(StorageArea.Local);
        Initialize(StorageArea.Session);
        // TODO P2: Enable Sync and Managed areas as needed
        // Initialize(StorageArea.Sync);
        Initialize(StorageArea.Managed);
    }

    private void Initialize(StorageArea area = StorageArea.Local) {
        if (_initializedAreas.Contains(area)) {
            _logger.LogDebug(nameof(Initialize) + ": Storage area {Area} already initialized", area);
            return; // Result.Ok();
        }

        try {
            _logger.Log(ServiceLogLevel, nameof(Initialize) + ": Initializing {Area} storage change listener", area);

            // Register global listener only once (handles ALL storage areas)
            if (!_listenerRegistered) {
                _globalCallback = (changes, areaName) => {
                    OnStorageChanged(changes, areaName);
                };

                // Subscribe to chrome.storage.onChanged via WebExtensions.Net
                // This single listener receives events from ALL storage areas
                _webExtensionsApi.Storage.OnChanged.AddListener(_globalCallback);
                _listenerRegistered = true;
                _logger.LogInformation(nameof(Initialize) + ": Registered global storage change listener (WebExtensions.Net native)");
            }

            _initializedAreas.Add(area);
            _logger.LogInformation(nameof(Initialize) + ": Enabled change notifications for {Area} storage", area);

            return; // Result.Ok();
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(Initialize) + ": Failed to initialize {Area} storage", area);
            throw; //return Result.Fail(new StorageError($"Initialize {area} failed", ex));
        }
    }

    private void OnStorageChanged(object changes, string areaName) {
        if (!Enum.TryParse<StorageArea>(areaName, true, out var area)) {
            _logger.LogWarning(nameof(OnStorageChanged) + ": Unknown storage area: {AreaName}", areaName);
            return;
        }

        // Only process if we initialized this area (acts as a filter)
        if (!_initializedAreas.Contains(area)) {
            _logger.LogTrace(nameof(OnStorageChanged) + ": Ignoring changes for non-initialized area {Area}", area);
            return;
        }

        try {
            // changes can be JsonElement or dictionary depending on WebExtensions.Net serialization
            Dictionary<string, JsonElement>? changesDict = null;

            if (changes is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object) {
                changesDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    jsonElement.GetRawText(), JsonOptions.Storage);
            }
            else if (changes is IDictionary<string, object> dict) {
                // Convert to JsonElement dictionary for uniform processing
                changesDict = dict.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value is JsonElement je ? je : JsonSerializer.SerializeToElement(kvp.Value, JsonOptions.Storage)
                );
            }
            else {
                throw new InvalidCastException($"Changes not a recognized type: {changes?.GetType().FullName}");
            }

            if (changesDict == null) {
                _logger.LogWarning(nameof(OnStorageChanged) + ": Failed to deserialize storage changes for {Area}", area);
                return;
            }

            _logger.Log(ServiceLogLevel, nameof(OnStorageChanged) + ": Storage changed in {Area}: {Keys}",
                area, string.Join(", ", changesDict.Keys));

            foreach (var (key, changeElement) in changesDict) {
                ProcessStorageChange(area, key, changeElement);
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(OnStorageChanged) + ": Error processing storage changes for {Area}", areaName);
        }
    }

    private void ProcessStorageChange(StorageArea area, string key, JsonElement changeElement) {
        try {
            // Extract newValue from StorageChange object
            // Chrome storage.onChanged provides: { key: { oldValue: ..., newValue: ... } }
            // When a key is deleted (via clear() or remove()), only oldValue is present
            var hasNewValue = changeElement.TryGetProperty("newValue", out var newValue);
            var hasOldValue = changeElement.TryGetProperty("oldValue", out var oldValue);

            if (!hasNewValue && !hasOldValue) {
                _logger.LogDebug(nameof(ProcessStorageChange) + ": No oldValue or newValue in change for {Key} in {Area} - ignoring", key, area);
                return;
            }

            // Get observers for this key
            if (_observersByArea.TryGetValue(area, out var observersByKey)
                && observersByKey.TryGetValue(key, out var entryList)) {

                if (hasNewValue) {
                    // Key was set or updated - notify with new value
                    NotifyObservers(key, area, entryList, newValue);
                }
                else {
                    // Key was deleted - notify with default value for the type
                    _logger.LogDebug(nameof(ProcessStorageChange) + ": Key {Key} deleted from {Area} storage - notifying observers with default value", key, area);
                    NotifyObserversOfDeletion(key, area, entryList);
                }
            }
            else {
                _logger.LogTrace(nameof(ProcessStorageChange) + ": No observers for {Key} in {Area}", key, area);
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(ProcessStorageChange) + ": Error processing change for {Key} in {Area}", key, area);
        }
    }

    private void NotifyObservers(string key, StorageArea area, List<ObserverEntry> entryList, object newValue) {
        if (entryList.Count == 0) {
            return;
        }

        // Get element type from first entry (all entries for same key have same type)
        var elementType = entryList[0].ElementType;

        try {
            // Deserialize newValue to appropriate type
            object? typedValue = null;
            if (newValue is JsonElement jsonElement) {
                typedValue = JsonSerializer.Deserialize(jsonElement.GetRawText(), elementType, JsonOptions.Storage);
            }
            else {
                typedValue = newValue;
            }

            if (typedValue != null) {
                // Create a snapshot to avoid concurrent modification if callbacks modify subscriptions
                var snapshot = entryList.ToArray();
                // Notify each observer using its typed callback
                foreach (var entry in snapshot) {
                    try {
                        entry.NotifyCallback(typedValue);
                        _logger.LogDebug(nameof(NotifyObservers) + ": Notified observer of {Key} change in {Area}", key, area);
                    }
                    catch (Exception ex) {
                        _logger.LogError(ex, nameof(NotifyObservers) + ": Observer error for {Key} in {Area}", key, area);
                    }
                }
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(NotifyObservers) + ": Failed to deserialize {Key} for observers", key);
        }
    }

    private void NotifyObserversOfDeletion(string key, StorageArea area, List<ObserverEntry> entryList) {
        if (entryList.Count == 0) {
            return;
        }

        // Get element type from first entry (all entries for same key have same type)
        var elementType = entryList[0].ElementType;

        try {
            // Create default instance for the type
            // This matches the behavior of GetItem<T> which returns default(T) when key not found
            var defaultValue = Activator.CreateInstance(elementType);

            if (defaultValue != null) {
                // Create a snapshot to avoid concurrent modification if callbacks modify subscriptions
                var snapshot = entryList.ToArray();
                // Notify each observer using its typed callback
                foreach (var entry in snapshot) {
                    try {
                        entry.NotifyCallback(defaultValue);
                        _logger.LogDebug(nameof(NotifyObserversOfDeletion) + ": Notified observer of {Key} deletion in {Area} with default value", key, area);
                    }
                    catch (Exception ex) {
                        _logger.LogError(ex, nameof(NotifyObserversOfDeletion) + ": Observer error for {Key} deletion in {Area}", key, area);
                    }
                }
            }
            else {
                _logger.LogWarning(nameof(NotifyObserversOfDeletion) + ": Could not create default instance of {Type} for {Key} deletion notification", elementType.Name, key);
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(NotifyObserversOfDeletion) + ": Failed to create default value for {Key} deletion", key);
        }
    }

    public async Task<Result> Clear(StorageArea area = StorageArea.Local) {
        var validation = StorageServiceValidation.ValidateOperation(nameof(Clear), area);
        if (validation.IsFailed) return validation;

        try {
            await ClearStorageArea(area);
            _logger.LogInformation(nameof(Clear) + ": Cleared {Area} storage", area);
            return Result.Ok();
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(Clear) + ": Failed to clear {Area} storage", area);
            return Result.Fail(new StorageError($"Clear {area} failed", ex));
        }
    }

    public async Task<Result<T?>> GetItem<T>(StorageArea area = StorageArea.Local) {
        var key = typeof(T).Name;

        try {
            var jsonElement = await GetFromStorageArea(area, key);

            if (jsonElement.TryGetProperty(Encoding.UTF8.GetBytes(key), out var element)) {
                var value = JsonSerializer.Deserialize<T>(element, JsonOptions.Storage);
                _logger.LogDebug(nameof(GetItem) + ": Retrieved {Key} from {Area} storage", key, area);
                return Result.Ok<T?>(value);
            }

            _logger.LogDebug(nameof(GetItem) + ": {Key} not found in {Area} storage", key, area);
            return Result.Ok<T?>(default);
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(GetItem) + ": Failed to get {Key} from {Area} storage", key, area);
            return Result.Fail<T?>(new StorageError($"Get {key} from {area} failed", ex));
        }
    }

    public async Task<Result> SetItem<T>(T value, StorageArea area = StorageArea.Local) {
        var validation = StorageServiceValidation.ValidateOperation(nameof(SetItem), area);
        if (validation.IsFailed) return validation;

        var key = typeof(T).Name;

        try {
            var data = new Dictionary<string, object?> { { key, value } };
            await SetInStorageArea(area, data);
            _logger.LogDebug(nameof(SetItem) + ": Set {Key} in {Area} storage", key, area);
            return Result.Ok();
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(SetItem) + ": Failed to set {Key} in {Area} storage", key, area);
            return Result.Fail(new StorageError($"Set {key} in {area} failed", ex));
        }
    }

    public async Task<Result> RemoveItem<T>(StorageArea area = StorageArea.Local) {
        var validation = StorageServiceValidation.ValidateOperation(nameof(RemoveItem), area);
        if (validation.IsFailed) return validation;

        var key = typeof(T).Name;

        try {
            await RemoveFromStorageArea(area, key);
            _logger.LogDebug(nameof(RemoveItem) + ": Removed {Key} from {Area} storage", key, area);
            return Result.Ok();
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(RemoveItem) + ": Failed to remove {Key} from {Area} storage", key, area);
            return Result.Fail(new StorageError($"Remove {key} from {area} failed", ex));
        }
    }

    public async Task<Result<string>> GetBackupItems(
        StorageArea area = StorageArea.Local,
        List<string>? excludeKeys = null
    ) {
        try {
            var areaName = area.ToString().ToLowerInvariant();
            var jsonDocument = await _jsRuntime.InvokeAsync<JsonDocument>(
                $"chrome.storage.{areaName}.get",
                (object?)null
            );

            // TODO P3: Apply default (or additional) exclusions if not provided
            // if (excludeKeys == null && area == StorageArea.Local) {
            //    excludeKeys = new List<string> { nameof(Whatever) };
            // }

            // Filter out excluded keys
            if (excludeKeys != null && excludeKeys.Count > 0) {
                jsonDocument = RemoveKeys(jsonDocument, excludeKeys);
            }

            var backupJson = jsonDocument.RootElement.GetRawText();
            _logger.LogInformation(nameof(GetBackupItems) + ": Created backup of {Area} storage ({Length} chars)",
                area, backupJson.Length);
            return Result.Ok(backupJson);
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(GetBackupItems) + ": Failed to backup {Area} storage", area);
            return Result.Fail(new StorageError($"Backup {area} failed", ex));
        }
    }

    public async Task<Result> RestoreBackupItems(
        string backupJson,
        StorageArea area = StorageArea.Local
    ) {
        var validation = StorageServiceValidation.ValidateOperation(nameof(RestoreBackupItems), area);
        if (validation.IsFailed) return validation;

        try {
            var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(backupJson, JsonOptions.Storage);
            if (data == null) {
                return Result.Fail(new StorageError("Invalid backup JSON: deserialized to null"));
            }

            // Convert JsonElement values to objects for storage API
            var storageData = data.ToDictionary(
                kvp => kvp.Key,
                kvp => (object?)kvp.Value
            );

            await SetInStorageArea(area, storageData);
            _logger.LogInformation(nameof(RestoreBackupItems) + ": Restored backup to {Area} storage ({Count} items)",
                area, data.Count);
            return Result.Ok();
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(RestoreBackupItems) + ": Failed to restore backup to {Area} storage", area);
            return Result.Fail(new StorageError($"Restore to {area} failed", ex));
        }
    }

    public async Task<Result<long>> GetBytesInUse(StorageArea area = StorageArea.Local) {
        var validation = StorageServiceValidation.ValidateOperation(nameof(GetBytesInUse), area);
        if (validation.IsFailed) {
            return StorageServiceValidation.ValidateAndFail<long>(nameof(GetBytesInUse), area);
        }

        try {
            var storage = GetStorageAreaWithUsage(area);
            var bytes = await storage.GetBytesInUse();
            return Result.Ok((long)bytes);
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(GetBytesInUse) + ": Failed to get bytes in use for {Area}", area);
            return Result.Fail(new StorageError($"GetBytesInUse {area} failed", ex));
        }
    }

    public async Task<Result<long>> GetBytesInUse<T>(StorageArea area = StorageArea.Local) {
        var validation = StorageServiceValidation.ValidateOperation(nameof(GetBytesInUse), area);
        if (validation.IsFailed) {
            return StorageServiceValidation.ValidateAndFail<long>(nameof(GetBytesInUse), area);
        }

        try {
            var storage = GetStorageAreaWithUsage(area);
            var key = typeof(T).Name;
            var keysParam = new WebExtensions.Net.Storage.StorageAreaWithUsageGetBytesInUseKeys(key);
            var bytes = await storage.GetBytesInUse(keysParam);
            return Result.Ok((long)bytes);
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(GetBytesInUse) + ": Failed to get bytes in use for {Type} in {Area}",
                typeof(T).Name, area);
            return Result.Fail(new StorageError($"GetBytesInUse<{typeof(T).Name}> {area} failed", ex));
        }
    }

    public async Task<Result<StorageQuota>> GetQuota(StorageArea area = StorageArea.Local) {
        var validation = StorageServiceValidation.ValidateOperation(nameof(GetQuota), area);
        if (validation.IsFailed) {
            return StorageServiceValidation.ValidateAndFail<StorageQuota>(nameof(GetQuota), area);
        }

        try {
            var usedResult = await GetBytesInUse(area);
            if (usedResult.IsFailed) {
                return Result.Fail<StorageQuota>(usedResult.Errors);
            }

            // NOTE: Quota values based on Chrome documentation, even though WebExtensions.Net
            // only provides GetBytesInUse for Session/Sync (not Local/Sync as Chrome docs say)
            var quota = new StorageQuota {
                UsedBytes = usedResult.Value,
                QuotaBytes = area switch {
                    StorageArea.Session => 10_485_760, // 10MB for session (Chrome 112+)
                    StorageArea.Sync => 102_400,       // 100KB for sync
                    _ => 0
                },
                MaxBytesPerItem = area == StorageArea.Sync ? 8_192 : null,  // 8KB per item for sync
                MaxItems = area == StorageArea.Sync ? 512 : null
            };

            return Result.Ok(quota);
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(GetQuota) + ": Failed to get quota for {Area}", area);
            return Result.Fail(new StorageError($"GetQuota {area} failed", ex));
        }
    }

    public IDisposable Subscribe<T>(
        IObserver<T> observer,
        StorageArea area = StorageArea.Local
    ) {
        var key = typeof(T).Name;

        if (!_initializedAreas.Contains(area)) {
            _logger.LogWarning(
                nameof(Subscribe) + ": Subscribing to {Key} in {Area} storage before Initialize() called. " +
                "Call Initialize({Area}) first to receive change notifications.",
                key, area, area
            );
        }

        // Create a typed callback that captures the generic type
        Action<object> notifyCallback = (obj) => {
            if (obj is T typedValue) {
                observer.OnNext(typedValue);
            }
            else {
                var error = new InvalidCastException(
                    $"Type mismatch when notifying observer for {key}: expected {typeof(T).Name}, got {obj?.GetType().Name ?? "null"}");
                _logger.LogError(error, "Failed to notify observer for {Key}", key);
                throw error;
            }
        };

        var entry = new ObserverEntry(observer, typeof(T), notifyCallback);

        // Get or create the inner dictionary for this area
        if (!_observersByArea.TryGetValue(area, out var observersByKey)) {
            observersByKey = [];
            _observersByArea[area] = observersByKey;
        }

        // Add the observer to the list
        if (!observersByKey.TryGetValue(key, out var list)) {
            list = [];
            observersByKey[key] = list;
        }

        if (!list.Any(e => ReferenceEquals(e.Observer, observer))) {
            list.Add(entry);
        }

        _logger.LogDebug(nameof(Subscribe) + ": Subscribed to {Key} in {Area} storage", key, area);

        // Fetch initial value and notify observer if non-null
        Task.Run(async () => {
            try {
                var result = await GetItem<T>(area);
                if (result.IsSuccess && result.Value is not null) {
                    observer.OnNext(result.Value);
                    _logger.LogDebug(nameof(Subscribe) + ": Sent initial value for {Key} in {Area} storage to new subscriber", key, area);
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, nameof(Subscribe) + ": Failed to fetch initial value for {Key} in {Area} storage", key, area);
            }
        });

        // Return unsubscriber that removes this observer
        return new UnsubscriberEntry(area, key, observer, RemoveObserver, () => {
            _logger.LogDebug(nameof(Subscribe) + ": Unsubscribed from {Key} in {Area} storage", key, area);
        });
    }

    /// <summary>
    /// Removes an observer from the observer collections.
    /// </summary>
    private void RemoveObserver(StorageArea area, string key, object observer) {
        if (!_observersByArea.TryGetValue(area, out var observersByKey)) {
            return; // Area not found, nothing to remove
        }

        if (observersByKey.TryGetValue(key, out var list)) {
            list.RemoveAll(e => ReferenceEquals(e.Observer, observer));
        }
    }

    // Helper methods to get the appropriate storage area API
    // Note: WebExtensions.Net has two separate types:
    // - StorageArea (for Session and Managed)
    // - StorageAreaWithUsage (for Local and Sync)
    // These do NOT inherit from each other - they are parallel types with overlapping methods.

    private async ValueTask ClearStorageArea(StorageArea area) {
        switch (area) {
            case StorageArea.Local:
                await _webExtensionsApi.Storage.Local.Clear();
                break;
            case StorageArea.Session:
                await _webExtensionsApi.Storage.Session.Clear();
                break;
            case StorageArea.Sync:
                await _webExtensionsApi.Storage.Sync.Clear();
                break;
            case StorageArea.Managed:
                await _webExtensionsApi.Storage.Managed.Clear();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown storage area");
        }
    }

    private async ValueTask<JsonElement> GetFromStorageArea(StorageArea area, string key) {
        switch (area) {
            case StorageArea.Local:
                return await _webExtensionsApi.Storage.Local.Get(
                    new WebExtensions.Net.Storage.StorageAreaGetKeys(key));
            case StorageArea.Session:
                return await _webExtensionsApi.Storage.Session.Get(
                    new WebExtensions.Net.Storage.StorageAreaWithUsageGetKeys(key));
            case StorageArea.Sync:
                return await _webExtensionsApi.Storage.Sync.Get(
                    new WebExtensions.Net.Storage.StorageAreaWithUsageGetKeys(key));
            case StorageArea.Managed:
                return await _webExtensionsApi.Storage.Managed.Get(
                    new WebExtensions.Net.Storage.StorageAreaGetKeys(key));
            default:
                throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown storage area");
        }
    }

    private async ValueTask SetInStorageArea(StorageArea area, Dictionary<string, object?> data) {
        switch (area) {
            case StorageArea.Local:
                await _webExtensionsApi.Storage.Local.Set(data);
                break;
            case StorageArea.Session:
                await _webExtensionsApi.Storage.Session.Set(data);
                break;
            case StorageArea.Sync:
                await _webExtensionsApi.Storage.Sync.Set(data);
                break;
            case StorageArea.Managed:
                await _webExtensionsApi.Storage.Managed.Set(data);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown storage area");
        }
    }

    private async ValueTask RemoveFromStorageArea(StorageArea area, string key) {
        switch (area) {
            case StorageArea.Local:
                await _webExtensionsApi.Storage.Local.Remove(
                    new WebExtensions.Net.Storage.StorageAreaRemoveKeys(key));
                break;
            case StorageArea.Session:
                await _webExtensionsApi.Storage.Session.Remove(
                    new WebExtensions.Net.Storage.StorageAreaWithUsageRemoveKeys(key));
                break;
            case StorageArea.Sync:
                await _webExtensionsApi.Storage.Sync.Remove(
                    new WebExtensions.Net.Storage.StorageAreaWithUsageRemoveKeys(key));
                break;
            case StorageArea.Managed:
                await _webExtensionsApi.Storage.Managed.Remove(
                    new WebExtensions.Net.Storage.StorageAreaRemoveKeys(key));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown storage area");
        }
    }

    private WebExtensions.Net.Storage.StorageAreaWithUsage GetStorageAreaWithUsage(StorageArea area) {
        // In WebExtensions.Net: Session and Sync are StorageAreaWithUsage (have GetBytesInUse)
        // Note: This differs from Chrome docs which say Local/Sync have quotas
        // We'll use validation layer to ensure only Local/Sync call this method
        return area switch {
            StorageArea.Session => _webExtensionsApi.Storage.Session,
            StorageArea.Sync => _webExtensionsApi.Storage.Sync,
            _ => throw new ArgumentOutOfRangeException(nameof(area), area,
                "Storage area doesn't support usage tracking in WebExtensions.Net")
        };
    }

    private static JsonDocument RemoveKeys(JsonDocument jsonDocument, List<string> keysToRemove) {
        var dict = new Dictionary<string, JsonElement>();
        foreach (var property in jsonDocument.RootElement.EnumerateObject()) {
            if (!keysToRemove.Contains(property.Name, StringComparer.OrdinalIgnoreCase)) {
                dict[property.Name] = property.Value;
            }
        }

        var filteredJson = JsonSerializer.Serialize(dict);
        return JsonDocument.Parse(filteredJson);
    }

    public void Dispose() {
        // Remove global listener if registered
        if (_listenerRegistered && _globalCallback != null) {
            try {
                _webExtensionsApi.Storage.OnChanged.RemoveListener(_globalCallback);
                _logger.LogDebug(nameof(Dispose) + ": Removed global storage change listener");
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, nameof(Dispose) + ": Failed to remove global storage listener");
            }
            _listenerRegistered = false;
            _globalCallback = null;
        }

        _initializedAreas.Clear();
        _observersByArea.Clear();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Unsubscriber for observer removal.
    /// Calls the removal callback to remove the observer from collections.
    /// </summary>
    private sealed class UnsubscriberEntry(
        StorageArea area,
        string key,
        object observer,
        Action<StorageArea, string, object> removeCallback,
        Action? onDispose = null
    ) : IDisposable {
        public void Dispose() {
            // Use the removal callback
            removeCallback(area, key, observer);
            onDispose?.Invoke();
        }
    }
}
