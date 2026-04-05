namespace Extension.Tests.Services.Storage;

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Extension.Models.Storage;
using Extension.Services.Storage;
using Extension.Tests.Models;
using Microsoft.Extensions.Logging;
using Moq;
using WebExtensions.Net.Mock;
using Xunit;

/// <summary>
/// Unit tests for StorageGateway's Phase 5 change-dispatch path — both per-record
/// Subscribe&lt;T&gt; observers (live change notifications, which became usable in Phase 5)
/// and the new SubscribeBatch batch observers.
///
/// These tests follow the same pattern as StorageServiceNotificationTests: reflect on
/// the private _globalCallback field to simulate chrome.storage.onChanged events from
/// the browser, since the real WebExtensions.Net event plumbing requires a browser.
///
/// Runs in the same Sequential collection as StorageServiceNotificationTests to avoid
/// race conditions with the shared WebExtensions.Net mock infrastructure.
/// </summary>
[Collection("StorageService Sequential Tests")]
public class StorageGatewayNotificationTests {
    private readonly MockJsRuntimeAdapter _mockJsRuntimeAdapter;
    private readonly StorageGateway _sut;

    private sealed record StorageChange<T>(
        [property: JsonPropertyName("newValue")] T? NewValue,
        [property: JsonPropertyName("oldValue")] T? OldValue);

    private static readonly JsonSerializerOptions JsonOpts = new() {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
        PropertyNamingPolicy = null,
    };

    public StorageGatewayNotificationTests() {
        _mockJsRuntimeAdapter = new MockJsRuntimeAdapter();
        _sut = new StorageGateway(_mockJsRuntimeAdapter, new Mock<ILogger<StorageGateway>>().Object);
    }

    // -------- Per-record Subscribe<T> live notifications --------

    [Fact]
    public void Subscribe_WhenChangeEventArrives_ObserverReceivesNewValue() {
        var observer = new Mock<IObserver<TestPasscodeModel>>();
        TestPasscodeModel? received = null;
        observer.Setup(x => x.OnNext(It.IsAny<TestPasscodeModel>()))
            .Callback<TestPasscodeModel>(v => received = v);

        using var sub = _sut.Subscribe(observer.Object, StorageArea.Local);

        var updated = new TestPasscodeModel { Passcode = "abc", SessionExpirationUtc = DateTime.UtcNow };
        TriggerStorageChange("local", nameof(TestPasscodeModel), updated, oldValue: default(TestPasscodeModel));

        Assert.NotNull(received);
        Assert.Equal("abc", received!.Passcode);
    }

    [Fact]
    public void Subscribe_DeletionEvent_NotifiesObserverWithDefaultInstance() {
        var observer = new Mock<IObserver<TestPasscodeModel>>();
        TestPasscodeModel? received = null;
        observer.Setup(x => x.OnNext(It.IsAny<TestPasscodeModel>()))
            .Callback<TestPasscodeModel>(v => received = v);

        using var sub = _sut.Subscribe(observer.Object, StorageArea.Session);

        TriggerDeletionEvent("session", nameof(TestPasscodeModel),
            oldValue: new TestPasscodeModel { Passcode = "gone", SessionExpirationUtc = DateTime.UtcNow });

        // Deletion delivers a default/uninitialized instance of the type.
        // TestPasscodeModel has a required property, so Activator.CreateInstance will throw
        // and we fall back to GetUninitializedObject. Either way, observer.OnNext is called.
        Assert.NotNull(received);
    }

    [Fact]
    public void Subscribe_AfterDispose_NoLongerReceivesNotifications() {
        var observer = new Mock<IObserver<TestPasscodeModel>>();
        var sub = _sut.Subscribe(observer.Object, StorageArea.Local);
        sub.Dispose();

        var updated = new TestPasscodeModel { Passcode = "x", SessionExpirationUtc = DateTime.UtcNow };
        TriggerStorageChange("local", nameof(TestPasscodeModel), updated, default);

        observer.Verify(x => x.OnNext(It.IsAny<TestPasscodeModel>()), Times.Never);
    }

    [Fact]
    public void Subscribe_DifferentAreas_OnlyMatchingAreaReceivesEvent() {
        var localObs = new Mock<IObserver<TestPasscodeModel>>();
        var sessionObs = new Mock<IObserver<TestPasscodeModel>>();
        using var s1 = _sut.Subscribe(localObs.Object, StorageArea.Local);
        using var s2 = _sut.Subscribe(sessionObs.Object, StorageArea.Session);

        var updated = new TestPasscodeModel { Passcode = "x", SessionExpirationUtc = DateTime.UtcNow };
        TriggerStorageChange("local", nameof(TestPasscodeModel), updated, default);

        localObs.Verify(x => x.OnNext(It.IsAny<TestPasscodeModel>()), Times.Once);
        sessionObs.Verify(x => x.OnNext(It.IsAny<TestPasscodeModel>()), Times.Never);
    }

    // -------- Batch observer (SubscribeBatch) --------

    [Fact]
    public void SubscribeBatch_SingleKeyChange_BatchObserverReceivesOneBatch() {
        var batchObs = new Mock<IStorageBatchObserver>();
        StorageChangeBatch? received = null;
        batchObs.Setup(x => x.OnBatch(It.IsAny<StorageArea>(), It.IsAny<StorageChangeBatch>()))
            .Callback<StorageArea, StorageChangeBatch>((_, b) => received = b);

        using var sub = _sut.SubscribeBatch(batchObs.Object, StorageArea.Local);

        var updated = new TestPasscodeModel { Passcode = "one", SessionExpirationUtc = DateTime.UtcNow };
        TriggerStorageChange("local", nameof(TestPasscodeModel), updated, default);

        Assert.NotNull(received);
        Assert.True(received!.Contains<TestPasscodeModel>());
        Assert.Single(received.ChangedKeys);
        var fromBatch = received.GetNew<TestPasscodeModel>();
        Assert.NotNull(fromBatch);
        Assert.Equal("one", fromBatch!.Passcode);
    }

    [Fact]
    public void SubscribeBatch_MultiKeyChange_BatchObserverSeesAllKeysInOneCall() {
        var batchObs = new Mock<IStorageBatchObserver>();
        var callCount = 0;
        StorageChangeBatch? received = null;
        batchObs.Setup(x => x.OnBatch(It.IsAny<StorageArea>(), It.IsAny<StorageChangeBatch>()))
            .Callback<StorageArea, StorageChangeBatch>((_, b) => { callCount++; received = b; });

        using var sub = _sut.SubscribeBatch(batchObs.Object, StorageArea.Local);

        // Simulate a single onChanged event with two keys (as produced by a single SetItems call).
        TriggerMultiKeyStorageChange("local", new Dictionary<string, object?> {
            [nameof(TestPasscodeModel)] = new TestPasscodeModel { Passcode = "A", SessionExpirationUtc = DateTime.UtcNow },
            [nameof(EnterprisePolicyConfig)] = new EnterprisePolicyConfig {
                KeriaAdminUrl = "https://example.com/admin"
            }
        });

        Assert.Equal(1, callCount); // One onChanged event = one batch observer call
        Assert.NotNull(received);
        Assert.Equal(2, received!.ChangedKeys.Count);
        Assert.True(received.Contains<TestPasscodeModel>());
        Assert.True(received.Contains<EnterprisePolicyConfig>());
        // Deserialize on demand — each record independently retrievable from the same batch.
        var prefs = received.GetNew<EnterprisePolicyConfig>();
        Assert.NotNull(prefs);
        Assert.Equal("https://example.com/admin", prefs!.KeriaAdminUrl);
    }

    [Fact]
    public void SubscribeBatch_DeletionEvent_IsDeletionReportsTrue() {
        var batchObs = new Mock<IStorageBatchObserver>();
        StorageChangeBatch? received = null;
        batchObs.Setup(x => x.OnBatch(It.IsAny<StorageArea>(), It.IsAny<StorageChangeBatch>()))
            .Callback<StorageArea, StorageChangeBatch>((_, b) => received = b);

        using var sub = _sut.SubscribeBatch(batchObs.Object, StorageArea.Session);

        TriggerDeletionEvent("session", nameof(TestPasscodeModel),
            oldValue: new TestPasscodeModel { Passcode = "gone", SessionExpirationUtc = DateTime.UtcNow });

        Assert.NotNull(received);
        Assert.True(received!.IsDeletion<TestPasscodeModel>());
        Assert.Null(received.GetNew<TestPasscodeModel>());
        var old = received.GetOld<TestPasscodeModel>();
        Assert.NotNull(old);
        Assert.Equal("gone", old!.Passcode);
    }

    [Fact]
    public void SubscribeBatch_LocalAreaObserver_DoesNotReceiveSessionEvents() {
        var localBatch = new Mock<IStorageBatchObserver>();
        using var sub = _sut.SubscribeBatch(localBatch.Object, StorageArea.Local);

        var updated = new TestPasscodeModel { Passcode = "x", SessionExpirationUtc = DateTime.UtcNow };
        TriggerStorageChange("session", nameof(TestPasscodeModel), updated, default);

        localBatch.Verify(x => x.OnBatch(It.IsAny<StorageArea>(), It.IsAny<StorageChangeBatch>()), Times.Never);
    }

    [Fact]
    public void SubscribeBatch_AfterDispose_NoLongerReceivesEvents() {
        var batchObs = new Mock<IStorageBatchObserver>();
        var sub = _sut.SubscribeBatch(batchObs.Object, StorageArea.Local);
        sub.Dispose();

        var updated = new TestPasscodeModel { Passcode = "x", SessionExpirationUtc = DateTime.UtcNow };
        TriggerStorageChange("local", nameof(TestPasscodeModel), updated, default);

        batchObs.Verify(x => x.OnBatch(It.IsAny<StorageArea>(), It.IsAny<StorageChangeBatch>()), Times.Never);
    }

    [Fact]
    public void SubscribeBatch_NullObserver_Throws() {
        Assert.Throws<ArgumentNullException>(() => _sut.SubscribeBatch(null!, StorageArea.Local));
    }

    // -------- Test helpers (reflection into _globalCallback) --------

    private void TriggerStorageChange<T>(string areaName, string key, T newValue, T? oldValue) {
        var change = new StorageChange<T>(newValue, oldValue);
        var changes = new Dictionary<string, StorageChange<T>> { [key] = change };
        var json = JsonSerializer.Serialize(changes, JsonOpts);
        var element = JsonDocument.Parse(json).RootElement;
        InvokeGlobalCallback(element, areaName);
    }

    private void TriggerMultiKeyStorageChange(string areaName, Dictionary<string, object?> newValuesByKey) {
        // Build a batched onChanged payload: { key1: { newValue: ... }, key2: { newValue: ... } }
        var changes = new Dictionary<string, object>();
        foreach (var (key, value) in newValuesByKey) {
            changes[key] = new { newValue = value };
        }
        var json = JsonSerializer.Serialize(changes, JsonOpts);
        var element = JsonDocument.Parse(json).RootElement;
        InvokeGlobalCallback(element, areaName);
    }

    private void TriggerDeletionEvent<T>(string areaName, string key, T oldValue) {
        var changes = new Dictionary<string, object> {
            [key] = new { oldValue = oldValue }
        };
        var json = JsonSerializer.Serialize(changes, JsonOpts);
        var element = JsonDocument.Parse(json).RootElement;
        InvokeGlobalCallback(element, areaName);
    }

    private void InvokeGlobalCallback(object changes, string areaName) {
        var field = typeof(StorageGateway).GetField(
            "_globalCallback", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        var callback = field!.GetValue(_sut) as Action<object, string>;
        Assert.NotNull(callback);
        callback!.Invoke(changes, areaName);
    }
}
