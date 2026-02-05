namespace Extension.Tests.Services;

using Extension.Models;
using Extension.Models.Storage;
using Extension.Services;
using Extension.Services.Storage;
using FluentResults;
using JsBind.Net;
using Microsoft.Extensions.Logging;
using Moq;
using WebExtensions.Net.Mock;
using Xunit;

/// <summary>
/// Unit tests for SessionManager.
/// Tests focus on testable methods - particularly ClearSessionForConfigChangeAsync.
///
/// Note: Full integration testing of alarm scheduling requires browser environment.
/// </summary>
public class SessionManagerTests {
    private readonly Mock<IStorageService> _mockStorageService;
    private readonly Mock<ILogger<SessionManager>> _mockLogger;
    private readonly MockJsRuntimeAdapter _mockJsRuntimeAdapter;

    public SessionManagerTests() {
        _mockStorageService = new Mock<IStorageService>();
        _mockLogger = new Mock<ILogger<SessionManager>>();
        _mockJsRuntimeAdapter = new MockJsRuntimeAdapter();

        // Setup default behaviors for storage service to prevent startup errors
        SetupDefaultStorageMocks();
    }

    private void SetupDefaultStorageMocks() {
        // Mock GetItem<PasscodeModel> to return not found (session locked state)
        _mockStorageService
            .Setup(s => s.GetItem<PasscodeModel>(StorageArea.Session))
            .ReturnsAsync(Result.Fail<PasscodeModel?>("Not found"));

        // Mock GetItem<Preferences> to return default preferences
        _mockStorageService
            .Setup(s => s.GetItem<Preferences>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<Preferences?>(new Preferences { IsStored = true }));

        // Mock GetItem<KeriaConnectConfigs> to return empty configs
        _mockStorageService
            .Setup(s => s.GetItem<KeriaConnectConfigs>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<KeriaConnectConfigs?>(new KeriaConnectConfigs { IsStored = true }));

        // Mock Subscribe to return a disposable
        _mockStorageService
            .Setup(s => s.Subscribe(It.IsAny<IObserver<PasscodeModel>>(), StorageArea.Session))
            .Returns(Mock.Of<IDisposable>());

        _mockStorageService
            .Setup(s => s.Subscribe(It.IsAny<IObserver<Preferences>>(), StorageArea.Local))
            .Returns(Mock.Of<IDisposable>());
    }

    #region ClearSessionForConfigChangeAsync Tests

    [Fact]
    public async Task ClearSessionForConfigChangeAsync_CallsClearOnSessionStorage() {
        // Arrange
        _mockStorageService
            .Setup(s => s.Clear(StorageArea.Session))
            .ReturnsAsync(Result.Ok());

        var sut = new SessionManager(
            _mockLogger.Object,
            _mockStorageService.Object,
            _mockJsRuntimeAdapter
        );

        // Allow time for async initialization to start
        await Task.Delay(100);

        // Act
        await sut.ClearSessionForConfigChangeAsync();

        // Assert
        _mockStorageService.Verify(
            s => s.Clear(StorageArea.Session),
            Times.Once,
            "ClearSessionForConfigChangeAsync should call Clear on Session storage area"
        );
    }

    [Fact]
    public async Task ClearSessionForConfigChangeAsync_ThrowsOnStorageFailure() {
        // Arrange
        _mockStorageService
            .Setup(s => s.Clear(StorageArea.Session))
            .ReturnsAsync(Result.Fail("Storage error"));

        var sut = new SessionManager(
            _mockLogger.Object,
            _mockStorageService.Object,
            _mockJsRuntimeAdapter
        );

        // Allow time for async initialization to start
        await Task.Delay(100);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ClearSessionForConfigChangeAsync()
        );

        Assert.Contains("Failed to clear session storage", exception.Message);
    }

    [Fact]
    public async Task ClearSessionForConfigChangeAsync_DoesNotClearLocalStorage() {
        // Arrange
        _mockStorageService
            .Setup(s => s.Clear(StorageArea.Session))
            .ReturnsAsync(Result.Ok());

        var sut = new SessionManager(
            _mockLogger.Object,
            _mockStorageService.Object,
            _mockJsRuntimeAdapter
        );

        // Allow time for async initialization to start
        await Task.Delay(100);

        // Act
        await sut.ClearSessionForConfigChangeAsync();

        // Assert - verify Local storage was NOT cleared
        _mockStorageService.Verify(
            s => s.Clear(StorageArea.Local),
            Times.Never,
            "ClearSessionForConfigChangeAsync should NOT clear Local storage (KeriaConnectConfigs, Preferences)"
        );
    }

    [Fact]
    public async Task ClearSessionForConfigChangeAsync_DoesNotClearSyncStorage() {
        // Arrange
        _mockStorageService
            .Setup(s => s.Clear(StorageArea.Session))
            .ReturnsAsync(Result.Ok());

        var sut = new SessionManager(
            _mockLogger.Object,
            _mockStorageService.Object,
            _mockJsRuntimeAdapter
        );

        // Allow time for async initialization to start
        await Task.Delay(100);

        // Act
        await sut.ClearSessionForConfigChangeAsync();

        // Assert - verify Sync storage was NOT cleared (contains passkeys)
        _mockStorageService.Verify(
            s => s.Clear(StorageArea.Sync),
            Times.Never,
            "ClearSessionForConfigChangeAsync should NOT clear Sync storage (contains passkeys)"
        );
    }

    #endregion
}
