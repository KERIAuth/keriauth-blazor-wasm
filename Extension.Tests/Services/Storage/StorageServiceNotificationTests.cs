namespace Extension.Tests.Services.Storage;

using Extension.Helper;
using Extension.Services.Storage;
using Extension.Models;
using Extension.Models.Storage;
using Extension.Tests.Models;
using FluentResults;
using JsBind.Net;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Moq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using WebExtensions.Net;
using WebExtensions.Net.Mock;
using Xunit;

/// <summary>
/// Unit tests for StorageService observable pattern and notification system.
/// Tests verify that observers receive OnNext() callbacks when storage changes.
///
/// These tests use reflection to access the private _globalCallback field and simulate
/// storage.onChanged events from the browser, following the WebExtensions.Net pattern.
///
/// NOTE: Tests in this class must run sequentially (not in parallel) to avoid race conditions
/// with the shared WebExtensions.Net mock infrastructure. The Collection attribute ensures
/// xUnit runs these tests one at a time.
/// </summary>
[Collection("StorageService Sequential Tests")]
public class StorageServiceNotificationTests {
    private readonly Mock<IJSRuntime> _mockJsRuntime;
    private readonly Mock<ILogger<StorageService>> _mockLogger;
    private readonly MockJsRuntimeAdapter _mockJsRuntimeAdapter;
    private readonly StorageService _sut;

    /// <summary>
    /// Record representing Chrome storage.onChanged event structure.
    /// Format: { "key": { "newValue": value, "oldValue": oldValue } }
    /// Note: Property names must be camelCase to match Chrome storage.onChanged format.
    /// </summary>
    private sealed record StorageChange<T>(
        [property: JsonPropertyName("newValue")] T? NewValue,
        [property: JsonPropertyName("oldValue")] T? OldValue
    );

    /// <summary>
    /// JSON serialization options matching StorageService configuration.
    /// Must match the options in StorageService.cs for proper deserialization.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = false,
        IncludeFields = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper,
    };

    public StorageServiceNotificationTests() {
        _mockJsRuntime = new Mock<IJSRuntime>();
        _mockLogger = new Mock<ILogger<StorageService>>();
        _mockJsRuntimeAdapter = new MockJsRuntimeAdapter();

        _sut = new StorageService(
            _mockJsRuntime.Object,
            _mockJsRuntimeAdapter,
            _mockLogger.Object
        );
    }

    #region Notification Tests

    [Fact]
    public Task Subscribe_WhenStorageChanges_ObserverReceivesNotification() {
        // Arrange
        // await _sut.Initialize(StorageArea.Local);

        var observer = new Mock<IObserver<PasscodeModel>>();
        PasscodeModel? receivedValue = null;
        observer.Setup(x => x.OnNext(It.IsAny<PasscodeModel>()))
            .Callback<PasscodeModel>(value => receivedValue = value);

        var subscription = _sut.Subscribe(observer.Object, StorageArea.Local);

        // Act - Simulate storage.onChanged event from browser
        var updatedPasscode = new PasscodeModel { Passcode = "updated-passcode-123" };
        TriggerStorageChange("local", "PasscodeModel", updatedPasscode);

        // Assert
        Assert.NotNull(receivedValue);
        Assert.Equal("updated-passcode-123", receivedValue!.Passcode);
        observer.Verify(x => x.OnNext(It.IsAny<PasscodeModel>()), Times.Once);

        subscription.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public Task Subscribe_MultipleObservers_AllReceiveNotifications() {
        // Arrange
        // await _sut.Initialize(StorageArea.Local);

        var observer1 = new Mock<IObserver<PasscodeModel>>();
        var observer2 = new Mock<IObserver<PasscodeModel>>();

        var subscription1 = _sut.Subscribe(observer1.Object, StorageArea.Local);
        var subscription2 = _sut.Subscribe(observer2.Object, StorageArea.Local);

        // Act - Trigger storage change
        var updatedPasscode = new PasscodeModel { Passcode = "multi-observer-test" };
        TriggerStorageChange("local", "PasscodeModel", updatedPasscode);

        // Assert - Both observers should be notified
        observer1.Verify(x => x.OnNext(It.Is<PasscodeModel>(
            p => p.Passcode == "multi-observer-test")), Times.Once);
        observer2.Verify(x => x.OnNext(It.Is<PasscodeModel>(
            p => p.Passcode == "multi-observer-test")), Times.Once);

        subscription1.Dispose();
        subscription2.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public Task Subscribe_AfterUnsubscribe_NoLongerReceivesNotifications() {
        // Arrange
        // await _sut.Initialize(StorageArea.Local);

        var observer = new Mock<IObserver<PasscodeModel>>();
        var subscription = _sut.Subscribe(observer.Object, StorageArea.Local);

        // Act - Unsubscribe, then trigger change
        subscription.Dispose();

        var updatedPasscode = new PasscodeModel { Passcode = "after-unsubscribe" };
        TriggerStorageChange("local", "PasscodeModel", updatedPasscode);

        // Assert - Should NOT receive notification
        observer.Verify(x => x.OnNext(It.IsAny<PasscodeModel>()), Times.Never);
        return Task.CompletedTask;
    }

    [Fact]
    public Task Subscribe_DifferentTypes_OnlyMatchingTypeNotified() {
        // Arrange
        // await _sut.Initialize(StorageArea.Session);

        var passcodeObserver = new Mock<IObserver<PasscodeModel>>();
        var testModelObserver = new Mock<IObserver<TestModel>>();

        var subscription1 = _sut.Subscribe(passcodeObserver.Object, StorageArea.Session);
        var subscription2 = _sut.Subscribe(testModelObserver.Object, StorageArea.Session);

        // Act - Trigger change for PasscodeModel only
        var updatedPasscode = new PasscodeModel { Passcode = "type-specific-test" };
        TriggerStorageChange("session", "PasscodeModel", updatedPasscode);

        // Assert - Only PasscodeModel observer should be notified
        passcodeObserver.Verify(x => x.OnNext(It.IsAny<PasscodeModel>()), Times.Once);
        testModelObserver.Verify(x => x.OnNext(It.IsAny<TestModel>()), Times.Never);

        subscription1.Dispose();
        subscription2.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public Task Subscribe_DifferentStorageAreas_OnlyMatchingAreaNotified() {
        // Arrange
        // await _sut.Initialize(StorageArea.Local);
        // await _sut.Initialize(StorageArea.Session);

        var localObserver = new Mock<IObserver<PasscodeModel>>();
        var sessionObserver = new Mock<IObserver<PasscodeModel>>();

        var subscription1 = _sut.Subscribe(localObserver.Object, StorageArea.Local);
        var subscription2 = _sut.Subscribe(sessionObserver.Object, StorageArea.Session);

        // Act - Trigger change for Local storage only
        var updatedPasscode = new PasscodeModel { Passcode = "local-only-test" };
        TriggerStorageChange("local", "PasscodeModel", updatedPasscode);

        // Assert - Only Local observer should be notified
        localObserver.Verify(x => x.OnNext(It.IsAny<PasscodeModel>()), Times.Once);
        sessionObserver.Verify(x => x.OnNext(It.IsAny<PasscodeModel>()), Times.Never);

        subscription1.Dispose();
        subscription2.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public Task Subscribe_WithTestModel_ReceivesCorrectType() {
        // Arrange
        // await _sut.Initialize(StorageArea.Session);

        var observer = new Mock<IObserver<TestModel>>();
        TestModel? receivedValue = null;
        observer.Setup(x => x.OnNext(It.IsAny<TestModel>()))
            .Callback<TestModel>(value => receivedValue = value);

        var subscription = _sut.Subscribe(observer.Object, StorageArea.Session);

        // Act - Simulate storage change with TestModel
        var testDict = new RecursiveDictionary();
        testDict["key1"] = "value1";
        testDict["key2"] = 42;

        var updatedTestModel = new TestModel {
            BoolProperty = true,
            IntProperty = 123,
            FloatProperty = 45.67f,
            StringProperty = "test-value",
            NullableStringProperty = "optional-value",
            RecursiveDictionaryProperty = testDict
        };
        TriggerStorageChange("session", "TestModel", updatedTestModel);

        // Assert
        Assert.NotNull(receivedValue);
        Assert.True(receivedValue!.BoolProperty);
        Assert.Equal(123, receivedValue.IntProperty);
        Assert.Equal(45.67f, receivedValue.FloatProperty);
        Assert.Equal("test-value", receivedValue.StringProperty);
        Assert.Equal("optional-value", receivedValue.NullableStringProperty);
        Assert.NotNull(receivedValue.RecursiveDictionaryProperty);
        observer.Verify(x => x.OnNext(It.IsAny<TestModel>()), Times.Once);

        subscription.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public Task Subscribe_WithEnterprisePolicyConfig_ReceivesCorrectType() {
        // Arrange
        // await _sut.Initialize(StorageArea.Managed);

        var observer = new Mock<IObserver<EnterprisePolicyConfig>>();
        EnterprisePolicyConfig? receivedValue = null;
        observer.Setup(x => x.OnNext(It.IsAny<EnterprisePolicyConfig>()))
            .Callback<EnterprisePolicyConfig>(value => receivedValue = value);

        var subscription = _sut.Subscribe(observer.Object, StorageArea.Managed);

        // Act - Simulate IT admin updating enterprise policy
        var updatedPolicy = new EnterprisePolicyConfig {
            KeriaAdminUrl = "https://keria.company.com/admin",
            KeriaBootUrl = "https://keria.company.com/boot",
            UpdatedUtc = DateTime.UtcNow
        };
        TriggerStorageChange("managed", "EnterprisePolicyConfig", updatedPolicy);

        // Assert
        Assert.NotNull(receivedValue);
        Assert.Equal("https://keria.company.com/admin", receivedValue!.KeriaAdminUrl);
        Assert.Equal("https://keria.company.com/boot", receivedValue.KeriaBootUrl);
        observer.Verify(x => x.OnNext(It.IsAny<EnterprisePolicyConfig>()), Times.Once);

        subscription.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public Task Subscribe_ChangeWithoutNewValue_DoesNotNotify() {
        // Arrange
        // await _sut.Initialize(StorageArea.Local);

        var observer = new Mock<IObserver<PasscodeModel>>();
        var subscription = _sut.Subscribe(observer.Object, StorageArea.Local);

        // Act - Trigger change without newValue (e.g., item was removed)
        var changes = new Dictionary<string, object> {
            ["PasscodeModel"] = new {
                oldValue = new PasscodeModel { Passcode = "old" }
                // No newValue
            }
        };
        var changesJson = JsonSerializer.Serialize(changes);
        var changesElement = JsonDocument.Parse(changesJson).RootElement;

        InvokeGlobalCallback(changesElement, "local");

        // Assert - Should NOT notify (no newValue)
        observer.Verify(x => x.OnNext(It.IsAny<PasscodeModel>()), Times.Never);

        subscription.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public Task Subscribe_UninitializedArea_DoesNotReceiveNotifications() {
        // Arrange
        // await _sut.Initialize(StorageArea.Local);

        var sessionObserver = new Mock<IObserver<PasscodeModel>>();
        var subscription = _sut.Subscribe(sessionObserver.Object, StorageArea.Session);

        // Act - Trigger change for Session (not initialized, even though subscribed)
        var updatedPasscode = new PasscodeModel { Passcode = "uninitialized-area" };
        TriggerStorageChange("session", "PasscodeModel", updatedPasscode);

        // Assert - Should NOT notify (area not initialized)
        sessionObserver.Verify(x => x.OnNext(It.IsAny<PasscodeModel>()), Times.Never);

        subscription.Dispose();
        return Task.CompletedTask;
    }

    #endregion

    #region Lifecycle Tests - None → Value → Value2 → Deleted

    [Fact]
    public Task Subscribe_InitialValueCreation_ObserverReceivesNotification() {
        // Arrange
        // await _sut.Initialize(StorageArea.Session);

        var observer = new Mock<IObserver<PasscodeModel>>();
        PasscodeModel? receivedValue = null;
        observer.Setup(x => x.OnNext(It.IsAny<PasscodeModel>()))
            .Callback<PasscodeModel>(value => receivedValue = value);

        var subscription = _sut.Subscribe(observer.Object, StorageArea.Session);

        // Act - Simulate initial creation (none → value)
        // oldValue is null when creating for first time
        var initialPasscode = new PasscodeModel { Passcode = "first-value" };
        TriggerStorageChangeWithOldValue("session", "PasscodeModel", initialPasscode, oldValue: null);

        // Assert
        Assert.NotNull(receivedValue);
        Assert.Equal("first-value", receivedValue!.Passcode);
        observer.Verify(x => x.OnNext(It.IsAny<PasscodeModel>()), Times.Once);

        subscription.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public Task Subscribe_SequentialUpdates_ReceivesAllNotifications() {
        // Arrange
        // await _sut.Initialize(StorageArea.Local);

        var observer = new Mock<IObserver<PasscodeModel>>();
        var receivedValues = new List<PasscodeModel>();
        observer.Setup(x => x.OnNext(It.IsAny<PasscodeModel>()))
            .Callback<PasscodeModel>(value => receivedValues.Add(value));

        var subscription = _sut.Subscribe(observer.Object, StorageArea.Local);

        // Act - Simulate value1 → value2 → value3
        var value1 = new PasscodeModel { Passcode = "first" };
        var value2 = new PasscodeModel { Passcode = "second" };
        var value3 = new PasscodeModel { Passcode = "third" };

        TriggerStorageChange("local", "PasscodeModel", value1);
        TriggerStorageChange("local", "PasscodeModel", value2);
        TriggerStorageChange("local", "PasscodeModel", value3);

        // Assert - Received all three notifications in order
        Assert.Equal(3, receivedValues.Count);
        Assert.Equal("first", receivedValues[0].Passcode);
        Assert.Equal("second", receivedValues[1].Passcode);
        Assert.Equal("third", receivedValues[2].Passcode);
        observer.Verify(x => x.OnNext(It.IsAny<PasscodeModel>()), Times.Exactly(3));

        subscription.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public Task Subscribe_FullLifecycle_NoneToValueToDeleted() {
        // Arrange
        // await _sut.Initialize(StorageArea.Session);

        var observer = new Mock<IObserver<PasscodeModel>>();
        var receivedValues = new List<PasscodeModel?>();
        observer.Setup(x => x.OnNext(It.IsAny<PasscodeModel>()))
            .Callback<PasscodeModel>(value => receivedValues.Add(value));

        var subscription = _sut.Subscribe(observer.Object, StorageArea.Session);

        // Act - Full lifecycle: none → value → value2 → deleted

        // Step 1: Create initial value (none → value)
        var initialValue = new PasscodeModel { Passcode = "initial" };
        TriggerStorageChangeWithOldValue("session", "PasscodeModel", initialValue, oldValue: null);

        // Step 2: Update to new value (value → value2)
        var updatedValue = new PasscodeModel { Passcode = "updated" };
        TriggerStorageChangeWithOldValue("session", "PasscodeModel", updatedValue, oldValue: initialValue);

        // Step 3: Delete (value2 → none) - only oldValue, no newValue
        TriggerStorageChangeWithOnlyOldValue("session", "PasscodeModel", oldValue: updatedValue);

        // Assert - Received 2 notifications (create and update, but not delete)
        Assert.Equal(2, receivedValues.Count);
        Assert.Equal("initial", receivedValues[0]!.Passcode);
        Assert.Equal("updated", receivedValues[1]!.Passcode);

        // OnNext should be called twice (not for deletion)
        observer.Verify(x => x.OnNext(It.IsAny<PasscodeModel>()), Times.Exactly(2));

        subscription.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public Task Subscribe_DeletionEvent_DoesNotCallOnNext() {
        // Arrange
        // await _sut.Initialize(StorageArea.Local);

        var observer = new Mock<IObserver<PasscodeModel>>();
        var subscription = _sut.Subscribe(observer.Object, StorageArea.Local);

        // Act - Simulate deletion (value → none)
        // Only oldValue present, no newValue
        var oldValue = new PasscodeModel { Passcode = "deleted" };
        TriggerStorageChangeWithOnlyOldValue("local", "PasscodeModel", oldValue);

        // Assert - Should NOT call OnNext (deletion doesn't trigger notification)
        observer.Verify(x => x.OnNext(It.IsAny<PasscodeModel>()), Times.Never);

        // Note: IObserver has OnCompleted() and OnError() methods that could be used
        // for deletion/error scenarios, but StorageService currently only uses OnNext()

        subscription.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public Task Subscribe_RapidUpdates_AllNotificationsReceived() {
        // Arrange
        // await _sut.Initialize(StorageArea.Session);

        var observer = new Mock<IObserver<TestModel>>();
        var receivedCount = 0;
        observer.Setup(x => x.OnNext(It.IsAny<TestModel>()))
            .Callback<TestModel>(_ => receivedCount++);

        var subscription = _sut.Subscribe(observer.Object, StorageArea.Session);

        // Act - Simulate rapid sequential updates
        for (int i = 1; i <= 10; i++) {
            var testDict = new RecursiveDictionary();
            testDict["counter"] = i;

            var testModel = new TestModel {
                BoolProperty = i % 2 == 0,
                IntProperty = i,
                FloatProperty = i * 1.5f,
                StringProperty = $"update-{i}",
                NullableStringProperty = null,
                RecursiveDictionaryProperty = testDict
            };
            TriggerStorageChange("session", "TestModel", testModel);
        }

        // Assert - All 10 notifications received
        Assert.Equal(10, receivedCount);
        observer.Verify(x => x.OnNext(It.IsAny<TestModel>()), Times.Exactly(10));

        subscription.Dispose();
        return Task.CompletedTask;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Simulates a storage.onChanged event from the browser by invoking the
    /// registered callback with properly formatted change data.
    ///
    /// Chrome storage.onChanged format: { "key": { "newValue": value, "oldValue": oldValue } }
    /// Uses record type and JSON serialization to match real browser behavior.
    /// </summary>
    private void TriggerStorageChange<T>(string areaName, string key, T newValue) {
        TriggerStorageChangeWithOldValue(areaName, key, newValue, oldValue: default);
    }

    /// <summary>
    /// Simulates a storage.onChanged event with both newValue and oldValue.
    /// Used to test update scenarios (value1 → value2).
    /// </summary>
    private void TriggerStorageChangeWithOldValue<T>(string areaName, string key, T newValue, T? oldValue) {
        // Create change using record type with both values
        var change = new StorageChange<T>(newValue, oldValue);

        // Create changes dictionary with the key
        var changes = new Dictionary<string, StorageChange<T>> {
            [key] = change
        };

        // Serialize to JSON and parse back to JsonElement (matches browser behavior)
        var changesJson = JsonSerializer.Serialize(changes, JsonOptions);
        var changesElement = JsonDocument.Parse(changesJson).RootElement;

        InvokeGlobalCallback(changesElement, areaName);
    }

    /// <summary>
    /// Simulates a storage.onChanged event for deletion (only oldValue, no newValue).
    /// Used to test deletion scenarios (value → none).
    /// </summary>
    private void TriggerStorageChangeWithOnlyOldValue<T>(string areaName, string key, T oldValue) {
        // For deletion, only oldValue is present
        var changes = new Dictionary<string, object> {
            [key] = new {
                oldValue = oldValue
                // No newValue property
            }
        };

        var changesJson = JsonSerializer.Serialize(changes, JsonOptions);
        var changesElement = JsonDocument.Parse(changesJson).RootElement;

        InvokeGlobalCallback(changesElement, areaName);
    }

    /// <summary>
    /// Gets the private _globalCallback field using reflection.
    /// </summary>
    private Action<object, string>? GetGlobalCallback() {
        var storageServiceType = typeof(StorageService);
        var globalCallbackField = storageServiceType.GetField(
            "_globalCallback",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        Assert.NotNull(globalCallbackField); // Ensure field exists
        return globalCallbackField.GetValue(_sut) as Action<object, string>;
    }

    /// <summary>
    /// Uses reflection to access the private _globalCallback field and invoke it,
    /// simulating the browser firing a storage.onChanged event.
    /// </summary>
    private void InvokeGlobalCallback(object changes, string areaName) {
        var callback = GetGlobalCallback();
        Assert.NotNull(callback); // Ensure callback was registered

        // Invoke the callback (simulates browser event)
        callback.Invoke(changes, areaName);
    }

    #endregion
}

/// <summary>
/// Collection definition to force sequential execution of StorageServiceNotificationTests.
/// This prevents race conditions when multiple tests try to initialize the same StorageArea
/// or interact with shared WebExtensions.Net mock infrastructure.
/// </summary>
[CollectionDefinition("StorageService Sequential Tests", DisableParallelization = true)]
public class StorageServiceSequentialTests {
}
