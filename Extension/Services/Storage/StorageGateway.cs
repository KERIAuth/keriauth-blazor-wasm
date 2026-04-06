namespace Extension.Services.Storage;

using System.Text;
using System.Text.Json;
using Extension.Helper;
using Extension.Models;
using Extension.Models.Storage;
using FluentResults;
using JsBind.Net;
using Microsoft.Extensions.Logging;
using WebExtensions.Net;

/// <summary>
/// Batched-capable storage layer over chrome.storage.*. Introduced in parallel with
/// <see cref="StorageGateway"/> and intended to replace it incrementally. See
/// <see cref="IStorageGateway"/> for API details and migration notes.
///
/// Phase 2 scope: single-item operations (GetItem, SetItem, RemoveItem, Subscribe)
/// with the same semantics as the existing <see cref="StorageGateway"/>. Bulk read,
/// bulk write, write transactions, and batch observers are implemented in later phases
/// and currently throw <see cref="NotImplementedException"/>.
/// </summary>
public class StorageGateway : IStorageGateway, IDisposable {
    private readonly ILogger<StorageGateway> _logger;
    private readonly WebExtensionsApi _webExtensionsApi;

    // Per-record observer storage.
    // Outer key: StorageArea. Inner key: typeof(T).Name. Inner value: list of observer entries.
    private readonly Dictionary<StorageArea, Dictionary<string, List<ObserverEntry>>> _observersByArea = new();

    // Batch observers, keyed by area. Each entry sees every change in its area as one StorageChangeBatch.
    private readonly Dictionary<StorageArea, List<IStorageBatchObserver>> _batchObserversByArea = new();

    // Global chrome.storage.onChanged listener state. Registered lazily the first time
    // any observer (per-record or batch) is added, and removed in Dispose.
    private Action<object, string>? _globalCallback;
    private bool _listenerRegistered;

    private sealed record ObserverEntry(object Observer, Type ElementType, Action<object> NotifyCallback);

    /// <summary>
    /// Statically configurable log level for lifecycle messages (constructor, future Initialize).
    /// Mirrors <see cref="StorageGateway.ServiceLogLevel"/> so both services can be verbosity-tuned together.
    /// </summary>
    public static LogLevel ServiceLogLevel { get; set; } = LogLevel.Debug;

    public StorageGateway(
        IJsRuntimeAdapter jsRuntimeAdapter,
        ILogger<StorageGateway> logger
    ) {
        _logger = logger;
        _webExtensionsApi = new WebExtensionsApi(jsRuntimeAdapter);
        _logger.Log(ServiceLogLevel, nameof(StorageGateway) + ": constructor");
    }

    /// <summary>
    /// Registers the global chrome.storage.onChanged listener on first demand.
    /// Idempotent — subsequent calls are no-ops.
    ///
    /// Note: StorageGateway already registers its own identical listener. Chrome multicasts
    /// onChanged events to every listener, so both services receive each event independently
    /// and each dispatches to its own subscribers. No interference between them.
    /// </summary>
    private void EnsureListenerRegistered() {
        if (_listenerRegistered) return;
        _globalCallback = OnStorageChanged;
        _webExtensionsApi.Storage.OnChanged.AddListener(_globalCallback);
        _listenerRegistered = true;
        _logger.LogInformation(nameof(EnsureListenerRegistered) + ": Registered global storage change listener");
    }

    // ---------- Single-item operations ----------

    public async Task<Result<T?>> GetItem<T>(StorageArea area = StorageArea.Local)
        where T : class, IStorageModel {
        var key = typeof(T).Name;
        try {
            var jsonElement = await GetFromStorageArea(area, key);
            if (jsonElement.TryGetProperty(Encoding.UTF8.GetBytes(key), out var element)) {
                var value = JsonSerializer.Deserialize<T>(element, JsonOptions.Storage);

                if (value is IVersionedStorageModel versioned) {
                    var expected = StorageModelRegistry.GetExpectedVersion(key);
                    if (expected is not null && versioned.SchemaVersion != expected.Value) {
                        _logger.LogWarning(nameof(GetItem) + ": {Key} schema version mismatch (stored={Stored}, expected={Expected}). Returning default.",
                            key, versioned.SchemaVersion, expected.Value);
                        return Result.Ok<T?>(default);
                    }
                }

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

    public async Task<Result> SetItem<T>(T value, StorageArea area = StorageArea.Local)
        where T : class, IStorageModel {
        // Reuse StorageGatewayValidation: it keys on operation names that match IStorageGateway's methods.
        // SetItem is blocked on Managed storage because Managed is read-only from the extension's
        // perspective. Its contents are provisioned out-of-band by IT administrators via enterprise
        // deployment (Windows registry keys, or JSON/plist policy files on macOS/Linux) — never
        // written by the extension itself. Reads of Managed, however, are permitted and used for
        // EnterprisePolicyConfig.
        var validation = StorageGatewayValidation.ValidateOperation(nameof(IStorageGateway.SetItem), area);
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

    public async Task<Result> RemoveItem<T>(StorageArea area = StorageArea.Local)
        where T : class, IStorageModel {
        var validation = StorageGatewayValidation.ValidateOperation(nameof(IStorageGateway.RemoveItem), area);
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

    public async Task<Result> Clear(StorageArea area = StorageArea.Local) {
        var validation = StorageGatewayValidation.ValidateOperation(nameof(IStorageGateway.Clear), area);
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

    public async Task<Result<StorageItemStatus>> GetItemStatus<T>(StorageArea area = StorageArea.Local)
        where T : class, IVersionedStorageModel {
        var key = typeof(T).Name;
        try {
            var jsonElement = await GetFromStorageArea(area, key);
            if (!jsonElement.TryGetProperty(Encoding.UTF8.GetBytes(key), out var element)) {
                return Result.Ok(StorageItemStatus.NotFound);
            }

            var value = JsonSerializer.Deserialize<T>(element, JsonOptions.Storage);
            if (value is null) {
                return Result.Ok(StorageItemStatus.NotFound);
            }

            var expected = StorageModelRegistry.GetExpectedVersion(key);
            if (expected is not null && value.SchemaVersion != expected.Value) {
                return Result.Ok(StorageItemStatus.VersionMismatch);
            }

            return Result.Ok(StorageItemStatus.Found);
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(GetItemStatus) + ": Failed to probe {Key} in {Area} storage", key, area);
            return Result.Fail<StorageItemStatus>(new StorageError($"GetItemStatus {key} failed", ex));
        }
    }

    public IDisposable Subscribe<T>(IObserver<T> observer, StorageArea area = StorageArea.Local)
        where T : class, IStorageModel {
        EnsureListenerRegistered();
        var key = typeof(T).Name;

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

        if (!_observersByArea.TryGetValue(area, out var observersByKey)) {
            observersByKey = [];
            _observersByArea[area] = observersByKey;
        }
        if (!observersByKey.TryGetValue(key, out var list)) {
            list = [];
            observersByKey[key] = list;
        }
        if (!list.Any(e => ReferenceEquals(e.Observer, observer))) {
            list.Add(entry);
        }

        _logger.LogDebug(nameof(Subscribe) + ": Subscribed to {Key} in {Area} storage", key, area);

        // Fire-and-forget initial-value push, matching StorageGateway.Subscribe<T> semantics.
        _ = Task.Run(async () => {
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

        return new UnsubscriberEntry(area, key, observer, RemoveObserver);
    }

    private void RemoveObserver(StorageArea area, string key, object observer) {
        if (!_observersByArea.TryGetValue(area, out var observersByKey)) return;
        if (observersByKey.TryGetValue(key, out var list)) {
            list.RemoveAll(e => ReferenceEquals(e.Observer, observer));
        }
    }

    // ---------- Bulk operations ----------

    public async Task<Result<StorageReadResult>> GetItems(StorageArea area, params Type[] types) {
        if (types is null || types.Length == 0) {
            // Empty request → empty result. No round-trip.
            return Result.Ok(new StorageReadResult(
                new Dictionary<string, JsonElement?>(),
                new HashSet<string>(),
                _logger));
        }

        // Validate that every requested type is a storage model. This keeps the public
        // signature (params Type[]) ergonomic while catching misuse at runtime.
        foreach (var t in types) {
            if (!typeof(IStorageModel).IsAssignableFrom(t)) {
                return Result.Fail<StorageReadResult>(new StorageError(
                    $"GetItems: type {t.FullName} does not implement IStorageModel"));
            }
        }

        var keys = types.Select(t => t.Name).Distinct().ToArray();

        try {
            var jsonObject = await GetManyFromStorageArea(area, keys);

            var rawByKey = new Dictionary<string, JsonElement?>(keys.Length);
            var versionMismatchKeys = new HashSet<string>();

            foreach (var t in types) {
                var key = t.Name;
                if (rawByKey.ContainsKey(key)) continue; // distinct across duplicates

                if (!jsonObject.TryGetProperty(key, out var element)) {
                    rawByKey[key] = null;
                    _logger.LogDebug(nameof(GetItems) + ": {Key} not found in {Area} storage", key, area);
                    continue;
                }

                // Version-mismatch peek (without full deserialization) for IVersionedStorageModel.
                if (typeof(IVersionedStorageModel).IsAssignableFrom(t)) {
                    var expected = StorageModelRegistry.GetExpectedVersion(key);
                    if (expected is not null && TryReadSchemaVersion(element, out var storedVersion)
                        && storedVersion != expected.Value) {
                        _logger.LogWarning(nameof(GetItems) + ": {Key} schema version mismatch (stored={Stored}, expected={Expected}). Discarding.",
                            key, storedVersion, expected.Value);
                        versionMismatchKeys.Add(key);
                        rawByKey[key] = element; // retained for IsVersionMismatch<T>() diagnostics
                        continue;
                    }
                }

                rawByKey[key] = element;
                _logger.LogDebug(nameof(GetItems) + ": Retrieved {Key} from {Area} storage", key, area);
            }

            return Result.Ok(new StorageReadResult(rawByKey, versionMismatchKeys, _logger));
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(GetItems) + ": Failed to bulk read {Keys} from {Area} storage",
                string.Join(",", keys), area);
            return Result.Fail<StorageReadResult>(new StorageError(
                $"GetItems {string.Join(",", keys)} from {area} failed", ex));
        }
    }

    private static bool TryReadSchemaVersion(JsonElement element, out int version) {
        // SchemaVersion property name is preserved as-is (PascalCase) because
        // JsonOptions.Storage uses PropertyNamingPolicy = null.
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("SchemaVersion", out var sv)
            && sv.ValueKind == JsonValueKind.Number
            && sv.TryGetInt32(out version)) {
            return true;
        }
        version = 0;
        return false;
    }

    public async Task<Result> SetItems(StorageArea area, params IStorageModel[] values) {
        // Reuse StorageGatewayValidation: SetItems is a multi-key form of SetItem,
        // so the same Managed-read-only rule applies.
        var validation = StorageGatewayValidation.ValidateOperation(nameof(IStorageGateway.SetItem), area);
        if (validation.IsFailed) return validation;

        if (values is null || values.Length == 0) {
            _logger.LogDebug(nameof(SetItems) + ": Called with no values — no-op");
            return Result.Ok();
        }

        // Build a dictionary keyed by the runtime type name of each value.
        // If a caller passes two values of the same type, the later one wins
        // (matching normal chrome.storage semantics for overwriting keys).
        var data = new Dictionary<string, object?>(values.Length);
        foreach (var value in values) {
            if (value is null) {
                return Result.Fail(new StorageError(
                    "SetItems: null value in values array — all values must be non-null IStorageModel instances"));
            }
            data[value.GetType().Name] = value;
        }

        try {
            await SetInStorageArea(area, data);
            _logger.LogDebug(nameof(SetItems) + ": Set {Count} keys [{Keys}] in {Area} storage",
                data.Count, string.Join(",", data.Keys), area);
            return Result.Ok();
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(SetItems) + ": Failed to bulk-set {Keys} in {Area} storage",
                string.Join(",", data.Keys), area);
            return Result.Fail(new StorageError(
                $"SetItems [{string.Join(",", data.Keys)}] in {area} failed", ex));
        }
    }

    public Task<Result> WriteTransaction(StorageArea area, Action<IStorageTransaction> build) {
        if (build is null) {
            return Task.FromResult<Result>(Result.Fail(new StorageError(
                "WriteTransaction: build action must not be null")));
        }

        // Collect .SetItem() calls from the builder, then delegate to SetItems for a single
        // atomic write. This keeps one implementation path for the underlying chrome.storage call.
        var tx = new StorageTransaction();
        try {
            build(tx);
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(WriteTransaction) + ": Exception thrown inside transaction builder");
            return Task.FromResult<Result>(Result.Fail(new StorageError(
                "WriteTransaction: exception thrown inside transaction builder", ex)));
        }

        return SetItems(area, tx.Values);
    }

    /// <summary>
    /// Mutable builder used by <see cref="WriteTransaction"/> to collect records
    /// before they are flushed as a single atomic <see cref="SetItems"/> call.
    /// </summary>
    private sealed class StorageTransaction : IStorageTransaction {
        private readonly Dictionary<string, IStorageModel> _staged = new();

        public IStorageModel[] Values => _staged.Values.ToArray();

        public void SetItem<T>(T value) where T : class, IStorageModel {
            ArgumentNullException.ThrowIfNull(value);
            // Last write wins, matching SetItems semantics.
            _staged[typeof(T).Name] = value;
        }
    }

    public IDisposable SubscribeBatch(IStorageBatchObserver observer, StorageArea area) {
        ArgumentNullException.ThrowIfNull(observer);

        EnsureListenerRegistered();

        if (!_batchObserversByArea.TryGetValue(area, out var list)) {
            list = new List<IStorageBatchObserver>();
            _batchObserversByArea[area] = list;
        }
        if (!list.Any(o => ReferenceEquals(o, observer))) {
            list.Add(observer);
        }

        _logger.LogDebug(nameof(SubscribeBatch) + ": Subscribed batch observer for {Area} storage", area);
        return new BatchUnsubscriber(area, observer, RemoveBatchObserver);
    }

    private void RemoveBatchObserver(StorageArea area, IStorageBatchObserver observer) {
        if (!_batchObserversByArea.TryGetValue(area, out var list)) return;
        list.RemoveAll(o => ReferenceEquals(o, observer));
    }

    // ---------- Change dispatch ----------

    /// <summary>
    /// Single entry point for chrome.storage.onChanged events. Parses the raw batch payload,
    /// then dispatches it to (a) every per-record observer whose key appears in the batch,
    /// and (b) every batch observer registered for the affected area.
    ///
    /// Per-record observers see one OnNext call per matching key (deletions notify with the
    /// type's default instance, matching deletion-default
    /// semantics). Batch observers see the entire batch in one OnBatch call.
    /// </summary>
    private void OnStorageChanged(object changes, string areaName) {
        if (!Enum.TryParse<StorageArea>(areaName, true, out var area)) {
            _logger.LogWarning(nameof(OnStorageChanged) + ": Unknown storage area: {AreaName}", areaName);
            return;
        }

        try {
            // WebExtensions.Net may deliver the changes payload as either a JsonElement
            // (object kind) or an IDictionary<string, object>, depending on transport.
            Dictionary<string, JsonElement>? changesDict = null;
            if (changes is JsonElement je && je.ValueKind == JsonValueKind.Object) {
                changesDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    je.GetRawText(), JsonOptions.Storage);
            }
            else if (changes is IDictionary<string, object> dict) {
                changesDict = dict.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value is JsonElement ele
                        ? ele
                        : JsonSerializer.SerializeToElement(kvp.Value, JsonOptions.Storage));
            }
            else {
                throw new InvalidCastException($"Changes payload not a recognized type: {changes?.GetType().FullName}");
            }

            if (changesDict is null || changesDict.Count == 0) {
                _logger.LogDebug(nameof(OnStorageChanged) + ": Empty or unparseable changes for {Area}", area);
                return;
            }

            _logger.Log(ServiceLogLevel, nameof(OnStorageChanged) + ": Storage changed in {Area}: {Keys}",
                area, string.Join(", ", changesDict.Keys));

            DispatchBatchObservers(area, changesDict);
            DispatchPerRecordObservers(area, changesDict);
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(OnStorageChanged) + ": Error processing storage changes for {Area}", areaName);
        }
    }

    private void DispatchBatchObservers(StorageArea area, Dictionary<string, JsonElement> changesDict) {
        if (!_batchObserversByArea.TryGetValue(area, out var observers) || observers.Count == 0) {
            return;
        }

        // Build StorageChangeBatch once per onChanged event; all batch observers in this area share it.
        var parsed = new Dictionary<string, (JsonElement? NewValue, JsonElement? OldValue)>(changesDict.Count);
        foreach (var (key, change) in changesDict) {
            JsonElement? newValue = null;
            JsonElement? oldValue = null;
            if (change.ValueKind == JsonValueKind.Object) {
                if (change.TryGetProperty("newValue", out var nv)) newValue = nv;
                if (change.TryGetProperty("oldValue", out var ov)) oldValue = ov;
            }
            // Skip entries that have neither side — nothing to deliver.
            if (newValue is null && oldValue is null) continue;
            parsed[key] = (newValue, oldValue);
        }

        if (parsed.Count == 0) return;

        var batch = new StorageChangeBatch(parsed, _logger);

        // Snapshot to tolerate observers that unsubscribe during dispatch.
        foreach (var observer in observers.ToArray()) {
            try {
                observer.OnBatch(area, batch);
            }
            catch (Exception ex) {
                _logger.LogError(ex, nameof(DispatchBatchObservers) + ": Batch observer threw for {Area}", area);
            }
        }
    }

    private void DispatchPerRecordObservers(StorageArea area, Dictionary<string, JsonElement> changesDict) {
        if (!_observersByArea.TryGetValue(area, out var observersByKey) || observersByKey.Count == 0) {
            return;
        }

        foreach (var (key, changeElement) in changesDict) {
            if (!observersByKey.TryGetValue(key, out var entryList) || entryList.Count == 0) continue;
            if (changeElement.ValueKind != JsonValueKind.Object) continue;

            var hasNewValue = changeElement.TryGetProperty("newValue", out var newValue);
            var hasOldValue = changeElement.TryGetProperty("oldValue", out _);

            if (!hasNewValue && !hasOldValue) continue;

            if (hasNewValue) {
                NotifyPerRecordObservers(area, key, entryList, newValue);
            }
            else {
                // Deletion: notify with the type's default instance, matching StorageGateway behavior.
                NotifyPerRecordObserversOfDeletion(area, key, entryList);
            }
        }
    }

    private void NotifyPerRecordObservers(StorageArea area, string key, List<ObserverEntry> entryList, JsonElement newValue) {
        var elementType = entryList[0].ElementType;
        object? typedValue;
        try {
            typedValue = JsonSerializer.Deserialize(newValue.GetRawText(), elementType, JsonOptions.Storage);
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(NotifyPerRecordObservers) + ": Failed to deserialize {Key}", key);
            return;
        }
        if (typedValue is null) return;

        foreach (var entry in entryList.ToArray()) {
            try {
                entry.NotifyCallback(typedValue);
            }
            catch (Exception ex) {
                _logger.LogError(ex, nameof(NotifyPerRecordObservers) + ": Observer error for {Key} in {Area}", key, area);
            }
        }
    }

    private void NotifyPerRecordObserversOfDeletion(StorageArea area, string key, List<ObserverEntry> entryList) {
        var elementType = entryList[0].ElementType;
        object? defaultValue;
        try {
            defaultValue = Activator.CreateInstance(elementType);
        }
        catch (MissingMethodException) {
            // Type has no parameterless constructor (e.g. positional records with required params).
            defaultValue = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(elementType);
        }

        if (defaultValue is null) return;

        foreach (var entry in entryList.ToArray()) {
            try {
                entry.NotifyCallback(defaultValue);
            }
            catch (Exception ex) {
                _logger.LogError(ex, nameof(NotifyPerRecordObserversOfDeletion) + ": Observer error for {Key} in {Area}", key, area);
            }
        }
    }

    // ---------- Low-level chrome.storage helpers ----------
    // Mirrors StorageGateway's private helpers. Kept local so StorageGateway has no
    // dependency on StorageGateway during the migration window.

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

    private async ValueTask<JsonElement> GetManyFromStorageArea(StorageArea area, IEnumerable<string> keys) {
        switch (area) {
            case StorageArea.Local:
                return await _webExtensionsApi.Storage.Local.Get(
                    new WebExtensions.Net.Storage.StorageAreaGetKeys(keys));
            case StorageArea.Session:
                return await _webExtensionsApi.Storage.Session.Get(
                    new WebExtensions.Net.Storage.StorageAreaWithUsageGetKeys(keys));
            case StorageArea.Sync:
                return await _webExtensionsApi.Storage.Sync.Get(
                    new WebExtensions.Net.Storage.StorageAreaWithUsageGetKeys(keys));
            case StorageArea.Managed:
                return await _webExtensionsApi.Storage.Managed.Get(
                    new WebExtensions.Net.Storage.StorageAreaGetKeys(keys));
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

    public void Dispose() {
        if (_listenerRegistered && _globalCallback is not null) {
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

        _observersByArea.Clear();
        _batchObserversByArea.Clear();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Unsubscriber for per-record observer removal.
    /// </summary>
    private sealed class UnsubscriberEntry(
        StorageArea area,
        string key,
        object observer,
        Action<StorageArea, string, object> removeCallback
    ) : IDisposable {
        public void Dispose() => removeCallback(area, key, observer);
    }

    /// <summary>
    /// Unsubscriber for batch observer removal.
    /// </summary>
    private sealed class BatchUnsubscriber(
        StorageArea area,
        IStorageBatchObserver observer,
        Action<StorageArea, IStorageBatchObserver> removeCallback
    ) : IDisposable {
        public void Dispose() => removeCallback(area, observer);
    }
}
