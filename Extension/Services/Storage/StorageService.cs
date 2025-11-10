namespace Extension.Services.Storage;

using FluentResults;
using Extension.Models;
using JsBind.Net;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Text;
using System.Text.Json;
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

    // Observer lists per area and type name
    // Outer key: StorageArea (e.g., Local, Session)
    // Inner key: Type name (e.g., "Preferences", "PasscodeModel")
    // Inner value: List of observer entries containing the observer and a typed notification callback
    // The callback avoids reflection by capturing the generic type at subscription time
    private readonly Dictionary<StorageArea, Dictionary<string, List<ObserverEntry>>> _observersByArea = new();

    // Helper record to store observer with its typed notification callback
    private sealed record ObserverEntry(object Observer, Type ElementType, Action<object> NotifyCallback);

    // Single global callback for ALL storage areas (chrome.storage.onChanged fires once for all areas)
    private Action<object, string>? _globalCallback;
    private bool _listenerRegistered;

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = false,
        IncludeFields = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper,
    };

    public static LogLevel ServiceLogLevel { get; set; } = LogLevel.Debug;

    public StorageService(
        IJSRuntime jsRuntime,
        IJsRuntimeAdapter jsRuntimeAdapter,
        ILogger<StorageService> logger
    ) {
        _jsRuntime = jsRuntime;
        _logger = logger;
        _webExtensionsApi = new WebExtensionsApi(jsRuntimeAdapter);
        _logger.Log(ServiceLogLevel, "StorageService: constructor");
    }

    public async Task<Result> Initialize(StorageArea area = StorageArea.Local) {
        if (_initializedAreas.Contains(area)) {
            _logger.LogDebug("Storage area {Area} already initialized", area);
            return Result.Ok();
        }

        try {
            _logger.Log(ServiceLogLevel, "Initializing {Area} storage change listener", area);

            // Register global listener only once (handles ALL storage areas)
            if (!_listenerRegistered) {
                _globalCallback = (changes, areaName) => {
                    OnStorageChanged(changes, areaName);
                };

                // Subscribe to chrome.storage.onChanged via WebExtensions.Net
                // This single listener receives events from ALL storage areas
                _webExtensionsApi.Storage.OnChanged.AddListener(_globalCallback);
                _listenerRegistered = true;
                _logger.LogInformation("Registered global storage change listener (WebExtensions.Net native)");
            }

            _initializedAreas.Add(area);
            _logger.LogInformation("Enabled change notifications for {Area} storage", area);

            return Result.Ok();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to initialize {Area} storage", area);
            return Result.Fail(new StorageError($"Initialize {area} failed", ex));
        }
    }

    private void OnStorageChanged(object changes, string areaName) {
        if (!Enum.TryParse<StorageArea>(areaName, true, out var area)) {
            _logger.LogWarning("Unknown storage area: {AreaName}", areaName);
            return;
        }

        // Only process if we initialized this area (acts as a filter)
        if (!_initializedAreas.Contains(area)) {
            _logger.LogTrace("Ignoring changes for non-initialized area {Area}", area);
            return;
        }

        try {
            // changes can be JsonElement or dictionary depending on WebExtensions.Net serialization
            Dictionary<string, JsonElement>? changesDict = null;

            if (changes is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object) {
                changesDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    jsonElement.GetRawText(), JsonOptions);
            } else if (changes is IDictionary<string, object> dict) {
                // Convert to JsonElement dictionary for uniform processing
                changesDict = dict.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value is JsonElement je ? je : JsonSerializer.SerializeToElement(kvp.Value, JsonOptions)
                );
            } else {
                throw new InvalidCastException($"Changes not a recognized type: {changes?.GetType().FullName}");
            }

            if (changesDict == null) {
                _logger.LogWarning("Failed to deserialize storage changes for {Area}", area);
                return;
            }

            _logger.Log(ServiceLogLevel, "Storage changed in {Area}: {Keys}",
                area, string.Join(", ", changesDict.Keys));

            foreach (var (key, changeElement) in changesDict) {
                ProcessStorageChange(area, key, changeElement);
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error processing storage changes for {Area}", areaName);
        }
    }

    private void ProcessStorageChange(StorageArea area, string key, JsonElement changeElement) {
        try {
            // Extract newValue from StorageChange object
            // Chrome storage.onChanged provides: { key: { oldValue: ..., newValue: ... } }
            if (!changeElement.TryGetProperty("newValue", out var newValue)) {
                _logger.LogDebug("No newValue in change for {Key} in {Area}", key, area);
                return;
            }

            // Notify type-specific observers
            if (_observersByArea.TryGetValue(area, out var observersByKey)
                && observersByKey.TryGetValue(key, out var entryList)) {
                NotifyObservers(key, area, entryList, newValue);
            } else {
                _logger.LogTrace("No observers for {Key} in {Area}", key, area);
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error processing change for {Key} in {Area}", key, area);
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
                typedValue = JsonSerializer.Deserialize(jsonElement.GetRawText(), elementType, JsonOptions);
            } else {
                typedValue = newValue;
            }

            if (typedValue != null) {
                // Notify each observer using its typed callback
                foreach (var entry in entryList) {
                    try {
                        entry.NotifyCallback(typedValue);
                        _logger.LogDebug("Notified observer of {Key} change in {Area}", key, area);
                    }
                    catch (Exception ex) {
                        _logger.LogError(ex, "Observer error for {Key} in {Area}", key, area);
                    }
                }
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to deserialize {Key} for observers", key);
        }
    }

    public async Task<Result> Clear(StorageArea area = StorageArea.Local) {
        var validation = StorageServiceValidation.ValidateOperation(nameof(Clear), area);
        if (validation.IsFailed) return validation;

        try {
            await ClearStorageArea(area);
            _logger.LogInformation("Cleared {Area} storage", area);
            return Result.Ok();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to clear {Area} storage", area);
            return Result.Fail(new StorageError($"Clear {area} failed", ex));
        }
    }

    public async Task<Result<T?>> GetItem<T>(StorageArea area = StorageArea.Local) {
        var key = typeof(T).Name;

        try {
            var jsonElement = await GetFromStorageArea(area, key);

            if (jsonElement.TryGetProperty(Encoding.UTF8.GetBytes(key), out var element)) {
                var value = JsonSerializer.Deserialize<T>(element, JsonOptions);
                _logger.LogDebug("Retrieved {Key} from {Area} storage", key, area);
                return Result.Ok<T?>(value);
            }

            _logger.LogDebug("{Key} not found in {Area} storage", key, area);
            return Result.Ok<T?>(default);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to get {Key} from {Area} storage", key, area);
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
            _logger.LogDebug("Set {Key} in {Area} storage", key, area);
            return Result.Ok();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to set {Key} in {Area} storage", key, area);
            return Result.Fail(new StorageError($"Set {key} in {area} failed", ex));
        }
    }

    public async Task<Result> RemoveItem<T>(StorageArea area = StorageArea.Local) {
        var validation = StorageServiceValidation.ValidateOperation(nameof(RemoveItem), area);
        if (validation.IsFailed) return validation;

        var key = typeof(T).Name;

        try {
            await RemoveFromStorageArea(area, key);
            _logger.LogDebug("Removed {Key} from {Area} storage", key, area);
            return Result.Ok();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to remove {Key} from {Area} storage", key, area);
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

            // Apply default exclusions if not provided
            if (excludeKeys == null && area == StorageArea.Local) {
                excludeKeys = new List<string> { nameof(AppState) };
            }

            // Filter out excluded keys
            if (excludeKeys != null && excludeKeys.Count > 0) {
                jsonDocument = RemoveKeys(jsonDocument, excludeKeys);
            }

            var backupJson = jsonDocument.RootElement.GetRawText();
            _logger.LogInformation("Created backup of {Area} storage ({Length} chars)",
                area, backupJson.Length);
            return Result.Ok(backupJson);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to backup {Area} storage", area);
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
            var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(backupJson, JsonOptions);
            if (data == null) {
                return Result.Fail(new StorageError("Invalid backup JSON: deserialized to null"));
            }

            // Convert JsonElement values to objects for storage API
            var storageData = data.ToDictionary(
                kvp => kvp.Key,
                kvp => (object?)kvp.Value
            );

            await SetInStorageArea(area, storageData);
            _logger.LogInformation("Restored backup to {Area} storage ({Count} items)",
                area, data.Count);
            return Result.Ok();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to restore backup to {Area} storage", area);
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
            _logger.LogError(ex, "Failed to get bytes in use for {Area}", area);
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
            _logger.LogError(ex, "Failed to get bytes in use for {Type} in {Area}",
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
            _logger.LogError(ex, "Failed to get quota for {Area}", area);
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
                "Subscribing to {Key} in {Area} storage before Initialize() called. " +
                "Call Initialize({Area}) first to receive change notifications.",
                key, area, area
            );
        }

        // Get or create observer dictionary for this area
        if (!_observersByArea.TryGetValue(area, out var observersByKey)) {
            observersByKey = new Dictionary<string, List<ObserverEntry>>();
            _observersByArea[area] = observersByKey;
        }

        // Get or create observer entry list for this key
        if (!observersByKey.TryGetValue(key, out var entryList)) {
            entryList = new List<ObserverEntry>();
            observersByKey[key] = entryList;
        }

        // Create a typed callback that captures the generic type
        // This avoids reflection when notifying observers
        Action<object> notifyCallback = (obj) => {
            if (obj is T typedValue) {
                observer.OnNext(typedValue);
            } else {
                var error = new InvalidCastException(
                    $"Type mismatch when notifying observer for {key}: expected {typeof(T).Name}, got {obj?.GetType().Name ?? "null"}");
                _logger.LogError(error, "Failed to notify observer for {Key}", key);
                throw error;
            }
        };

        var entry = new ObserverEntry(observer, typeof(T), notifyCallback);
        if (!entryList.Any(e => ReferenceEquals(e.Observer, observer))) {
            entryList.Add(entry);
            _logger.LogDebug("Subscribed to {Key} in {Area} storage", key, area);
        }

        return new UnsubscriberEntry(entryList, observer, () => {
            _logger.LogDebug("Unsubscribed from {Key} in {Area} storage", key, area);
        });
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
                _logger.LogDebug("Removed global storage change listener");
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to remove global storage listener");
            }
            _listenerRegistered = false;
            _globalCallback = null;
        }

        _initializedAreas.Clear();
        _observersByArea.Clear();

        GC.SuppressFinalize(this);
    }

    private sealed class UnsubscriberEntry(
        List<ObserverEntry> entries,
        object observer,
        Action? onDispose = null
    ) : IDisposable {
        public void Dispose() {
            var entry = entries.FirstOrDefault(e => ReferenceEquals(e.Observer, observer));
            if (entry != null) {
                entries.Remove(entry);
            }
            onDispose?.Invoke();
        }
    }
}
