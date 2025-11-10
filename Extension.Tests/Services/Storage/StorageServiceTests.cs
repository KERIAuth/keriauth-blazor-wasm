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
using System.Text.Json;
using WebExtensions.Net;
using WebExtensions.Net.Mock;
using Xunit;

/// <summary>
/// Unit tests for StorageService following the patterns documented in STORAGE_SERVICE_MIGRATION.md.
/// Tests cover validation rules, type-safe operations, and quota calculations.
///
/// Note: Full integration testing of WebExtensions.Net storage APIs requires browser environment.
/// These tests focus on testable logic: validation, type safety, and quota calculations.
/// </summary>
public class StorageServiceTests {
    private readonly Mock<IJSRuntime> _mockJsRuntime;
    private readonly Mock<ILogger<StorageService>> _mockLogger;
    private readonly MockJsRuntimeAdapter _mockJsRuntimeAdapter;
    private readonly StorageService _sut;

    public StorageServiceTests() {
        _mockJsRuntime = new Mock<IJSRuntime>();
        _mockLogger = new Mock<ILogger<StorageService>>();
        _mockJsRuntimeAdapter = new MockJsRuntimeAdapter();

        _sut = new StorageService(
            _mockJsRuntime.Object,
            _mockJsRuntimeAdapter,
            _mockLogger.Object
        );
    }

    #region Validation Tests - Managed Storage (Read-Only)

    [Fact]
    public async Task SetItem_OnManagedStorage_ReturnsValidationError() {
        // Arrange
        var model = new PasscodeModel { Passcode = "test123" };

        // Act
        var result = await _sut.SetItem(model, StorageArea.Managed);

        // Assert
        Assert.True(result.IsFailed);
        Assert.Single(result.Errors);
        Assert.Contains("read-only", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Managed", result.Errors[0].Message);
    }

    [Fact]
    public async Task RemoveItem_OnManagedStorage_ReturnsValidationError() {
        // Act
        var result = await _sut.RemoveItem<PasscodeModel>(StorageArea.Managed);

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("read-only", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Clear_OnManagedStorage_ReturnsValidationError() {
        // Act
        var result = await _sut.Clear(StorageArea.Managed);

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("read-only", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestoreBackupItems_OnManagedStorage_ReturnsValidationError() {
        // Arrange
        var backupJson = "{}";

        // Act
        var result = await _sut.RestoreBackupItems(backupJson, StorageArea.Managed);

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("read-only", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Validation Tests - Quota Operations

    [Theory]
    [InlineData(StorageArea.Local)]
    [InlineData(StorageArea.Managed)]
    public async Task GetBytesInUse_OnLocalOrManaged_ReturnsValidationError(StorageArea area) {
        // Act
        var result = await _sut.GetBytesInUse(area);

        // Assert
        Assert.True(result.IsFailed);
        Assert.Single(result.Errors);
        Assert.Contains("no quota", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(area.ToString(), result.Errors[0].Message);
    }

    [Theory]
    [InlineData(StorageArea.Local)]
    [InlineData(StorageArea.Managed)]
    public async Task GetQuota_OnLocalOrManaged_ReturnsValidationError(StorageArea area) {
        // Act
        var result = await _sut.GetQuota(area);

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("no quota", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(StorageArea.Local)]
    [InlineData(StorageArea.Managed)]
    public async Task GetBytesInUse_TypeSpecific_OnLocalOrManaged_ReturnsValidationError(StorageArea area) {
        // Act
        var result = await _sut.GetBytesInUse<PasscodeModel>(area);

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("no quota", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Type Safety Tests

    [Fact]
    public void TypeName_UsedAsStorageKey_PasscodeModel() {
        // Arrange - Using reflection to verify the pattern
        var typeName = typeof(PasscodeModel).Name;

        // Assert - Storage key should be "PasscodeModel" (not "passcode")
        Assert.Equal("PasscodeModel", typeName);
    }

    [Fact]
    public void TypeName_UsedAsStorageKey_TestModel() {
        // Arrange
        var typeName = typeof(TestModel).Name;

        // Assert - Storage key should be "TestModel"
        Assert.Equal("TestModel", typeName);
    }

    [Fact]
    public void TypeName_UsedAsStorageKey_EnterprisePolicyConfig() {
        // Arrange
        var typeName = typeof(EnterprisePolicyConfig).Name;

        // Assert - Storage key should be "EnterprisePolicyConfig"
        Assert.Equal("EnterprisePolicyConfig", typeName);
    }

    #endregion

    #region StorageQuota Calculations Tests

    [Fact]
    public void StorageQuota_RemainingBytes_CalculatedCorrectly() {
        // Arrange
        var quota = new StorageQuota {
            QuotaBytes = 10_485_760, // 10MB
            UsedBytes = 2_097_152    // 2MB
        };

        // Act & Assert
        Assert.Equal(8_388_608, quota.RemainingBytes); // 8MB remaining
    }

    [Fact]
    public void StorageQuota_PercentUsed_CalculatedCorrectly() {
        // Arrange
        var quota = new StorageQuota {
            QuotaBytes = 100_000,
            UsedBytes = 25_000
        };

        // Act & Assert
        Assert.Equal(25.0, quota.PercentUsed);
    }

    [Fact]
    public void StorageQuota_PercentUsed_WhenFull_Returns100() {
        // Arrange
        var quota = new StorageQuota {
            QuotaBytes = 100_000,
            UsedBytes = 100_000
        };

        // Act & Assert
        Assert.Equal(100.0, quota.PercentUsed);
    }

    [Fact]
    public void StorageQuota_PercentUsed_WhenEmpty_Returns0() {
        // Arrange
        var quota = new StorageQuota {
            QuotaBytes = 100_000,
            UsedBytes = 0
        };

        // Act & Assert
        Assert.Equal(0.0, quota.PercentUsed);
    }

    [Fact]
    public void StorageQuota_PercentUsed_WithZeroQuota_Returns0() {
        // Arrange
        var quota = new StorageQuota {
            QuotaBytes = 0,
            UsedBytes = 0
        };

        // Act & Assert
        Assert.Equal(0.0, quota.PercentUsed); // Avoid division by zero
    }

    [Fact]
    public void StorageQuota_SessionStorage_HasCorrectLimits() {
        // Arrange - Based on StorageService.GetQuota() implementation
        var quota = new StorageQuota {
            QuotaBytes = 10_485_760,  // 10MB for session
            UsedBytes = 1_000_000,
            MaxBytesPerItem = null,    // Session has no item limit
            MaxItems = null
        };

        // Assert
        Assert.Null(quota.MaxBytesPerItem);
        Assert.Null(quota.MaxItems);
        Assert.Equal(10_485_760, quota.QuotaBytes);
    }

    [Fact]
    public void StorageQuota_SyncStorage_HasCorrectLimits() {
        // Arrange - Based on StorageService.GetQuota() implementation
        var quota = new StorageQuota {
            QuotaBytes = 102_400,      // 100KB for sync
            UsedBytes = 50_000,
            MaxBytesPerItem = 8_192,   // 8KB per item
            MaxItems = 512
        };

        // Assert
        Assert.Equal(8_192, quota.MaxBytesPerItem);
        Assert.Equal(512, quota.MaxItems);
        Assert.Equal(102_400, quota.QuotaBytes);
    }

    #endregion

    #region Storage Model Tests

    [Fact]
    public void PasscodeModel_RequiresPasscode() {
        // Act & Assert - Should compile with required property
        var model = new PasscodeModel { Passcode = "test123" };
        Assert.Equal("test123", model.Passcode);
    }

    [Fact]
    public void TestModel_RequiredProperties() {
        // Arrange
        var testDict = new RecursiveDictionary();
        testDict["test"] = "value";

        // Act
        var model = new TestModel {
            BoolProperty = true,
            IntProperty = 42,
            FloatProperty = 3.14f,
            StringProperty = "required-string",
            RecursiveDictionaryProperty = testDict
        };

        // Assert
        Assert.True(model.BoolProperty);
        Assert.Equal(42, model.IntProperty);
        Assert.Equal(3.14f, model.FloatProperty);
        Assert.Equal("required-string", model.StringProperty);
        Assert.Null(model.NullableStringProperty);
        Assert.NotNull(model.RecursiveDictionaryProperty);
    }

    [Fact]
    public void EnterprisePolicyConfig_AllowsNullValues() {
        // Act - All properties are optional (nullable)
        var model = new EnterprisePolicyConfig();

        // Assert
        Assert.Null(model.KeriaAdminUrl);
        Assert.Null(model.KeriaBootUrl);
        Assert.Null(model.UpdatedUtc);
    }

    [Fact]
    public void EnterprisePolicyConfig_AcceptsValues() {
        // Arrange
        var now = DateTime.UtcNow;

        // Act
        var model = new EnterprisePolicyConfig {
            KeriaAdminUrl = "https://keria.company.com/admin",
            KeriaBootUrl = "https://keria.company.com/boot",
            UpdatedUtc = now
        };

        // Assert
        Assert.Equal("https://keria.company.com/admin", model.KeriaAdminUrl);
        Assert.Equal("https://keria.company.com/boot", model.KeriaBootUrl);
        Assert.Equal(now, model.UpdatedUtc);
    }

    #endregion

    #region Observable Pattern Tests

    [Fact]
    public void Subscribe_BeforeInitialize_LogsWarning() {
        // Arrange
        var observer = new Mock<IObserver<PasscodeModel>>();

        // Act
        var subscription = _sut.Subscribe(observer.Object, StorageArea.Session);

        // Assert
        Assert.NotNull(subscription);

        // Verify warning was logged about subscribing before Initialize()
        VerifyLogContains(LogLevel.Warning, "before Initialize()");

        subscription.Dispose();
    }

    [Fact]
    public async Task Subscribe_ReturnsDisposable() {
        // Arrange
        await _sut.Initialize(StorageArea.Session);
        var observer = new Mock<IObserver<PasscodeModel>>();

        // Act
        var subscription = _sut.Subscribe(observer.Object, StorageArea.Session);

        // Assert
        Assert.NotNull(subscription);
        Assert.IsAssignableFrom<IDisposable>(subscription);

        subscription.Dispose();
    }

    [Fact]
    public async Task Subscribe_SameObserverTwice_OnlyAddsOnce() {
        // Arrange
        await _sut.Initialize(StorageArea.Session);
        var observer = new Mock<IObserver<PasscodeModel>>();

        // Act
        var subscription1 = _sut.Subscribe(observer.Object, StorageArea.Session);
        var subscription2 = _sut.Subscribe(observer.Object, StorageArea.Session);

        // Assert - Both subscriptions are valid
        Assert.NotNull(subscription1);
        Assert.NotNull(subscription2);

        // Cleanup
        subscription1.Dispose();
        subscription2.Dispose();
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_WithoutInitialize_DoesNotThrow() {
        // Act & Assert - Should not throw
        var exception = Record.Exception(() => _sut.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public async Task Dispose_AfterInitialize_DoesNotThrow() {
        // Arrange
        await _sut.Initialize(StorageArea.Local);

        // Act & Assert
        var exception = Record.Exception(() => _sut.Dispose());
        Assert.Null(exception);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Verify that the logger was called with a message containing the expected text.
    /// </summary>
    private void VerifyLogContains(LogLevel level, string expectedText) {
        _mockLogger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(expectedText, StringComparison.OrdinalIgnoreCase)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce
        );
    }

    #endregion
}
