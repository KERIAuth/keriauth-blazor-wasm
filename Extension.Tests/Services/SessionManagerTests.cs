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
    private readonly Mock<IStorageGateway> _mockStorageGateway;
    private readonly Mock<ILogger<SessionManager>> _mockLogger;
    private readonly MockJsRuntimeAdapter _mockJsRuntimeAdapter;

    public SessionManagerTests() {
        _mockStorageGateway = new Mock<IStorageGateway>();
        _mockLogger = new Mock<ILogger<SessionManager>>();
        _mockJsRuntimeAdapter = new MockJsRuntimeAdapter();

        // Setup default behaviors for storage service to prevent startup errors
        SetupDefaultStorageMocks();
    }

    private void SetupDefaultStorageMocks() {
        // Mock GetItem<SessionStateModel> to return not found (session locked state)
        _mockStorageGateway
            .Setup(s => s.GetItem<SessionStateModel>(StorageArea.Session))
            .ReturnsAsync(Result.Fail<SessionStateModel?>("Not found"));

        // Mock GetItem<Preferences> to return default preferences
        _mockStorageGateway
            .Setup(s => s.GetItem<Preferences>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<Preferences?>(new Preferences { IsStored = true }));

        // Mock GetItem<KeriaConnectConfigs> to return empty configs
        _mockStorageGateway
            .Setup(s => s.GetItem<KeriaConnectConfigs>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<KeriaConnectConfigs?>(new KeriaConnectConfigs { IsStored = true }));

        // Mock Subscribe to return a disposable
        _mockStorageGateway
            .Setup(s => s.Subscribe(It.IsAny<IObserver<SessionStateModel>>(), StorageArea.Session))
            .Returns(Mock.Of<IDisposable>());

        _mockStorageGateway
            .Setup(s => s.Subscribe(It.IsAny<IObserver<Preferences>>(), StorageArea.Local))
            .Returns(Mock.Of<IDisposable>());

        // Mock bulk RemoveItems (used by ClearKeriaSessionRecordsAsync / ClearSessionForConfigChangeAsync)
        _mockStorageGateway
            .Setup(s => s.RemoveItems(StorageArea.Session, It.IsAny<Type[]>()))
            .ReturnsAsync(Result.Ok());
    }

    #region ClearSessionForConfigChangeAsync Tests

    [Fact]
    public async Task ClearSessionForConfigChangeAsync_RemovesSessionRecordsButPreservesBwReadyState() {
        // Arrange — RemoveItem mocks are in SetupDefaultStorageMocks

        var sut = new SessionManager(
            _mockLogger.Object,
            _mockStorageGateway.Object,
            _mockJsRuntimeAdapter
        );

        // Allow time for async initialization to start
        await Task.Delay(100);

        // Act
        await sut.ClearSessionForConfigChangeAsync();

        // Assert — bulk RemoveItems called once with session record types but NOT BwReadyState
        _mockStorageGateway.Verify(
            s => s.RemoveItems(StorageArea.Session, It.Is<Type[]>(types =>
                types.Contains(typeof(SessionStateModel)) &&
                types.Contains(typeof(KeriaConnectionInfo)) &&
                types.Contains(typeof(CachedIdentifiers)) &&
                types.Contains(typeof(PendingBwAppRequests)) &&
                !types.Contains(typeof(BwReadyState)))),
            Times.Once);

        // Assert — Clear is NOT called (selective removal instead)
        _mockStorageGateway.Verify(s => s.Clear(StorageArea.Session), Times.Never);
    }

    [Fact]
    public async Task ClearSessionForConfigChangeAsync_DoesNotClearLocalStorage() {
        // Arrange

        var sut = new SessionManager(
            _mockLogger.Object,
            _mockStorageGateway.Object,
            _mockJsRuntimeAdapter
        );

        // Allow time for async initialization to start
        await Task.Delay(100);

        // Act
        await sut.ClearSessionForConfigChangeAsync();

        // Assert - verify Local storage was NOT cleared
        _mockStorageGateway.Verify(
            s => s.Clear(StorageArea.Local),
            Times.Never,
            "ClearSessionForConfigChangeAsync should NOT clear Local storage (KeriaConnectConfigs, Preferences)"
        );
    }

    [Fact]
    public async Task ClearSessionForConfigChangeAsync_DoesNotClearSyncStorage() {
        // Arrange

        var sut = new SessionManager(
            _mockLogger.Object,
            _mockStorageGateway.Object,
            _mockJsRuntimeAdapter
        );

        // Allow time for async initialization to start
        await Task.Delay(100);

        // Act
        await sut.ClearSessionForConfigChangeAsync();

        // Assert - verify Sync storage was NOT cleared (contains passkeys)
        _mockStorageGateway.Verify(
            s => s.Clear(StorageArea.Sync),
            Times.Never,
            "ClearSessionForConfigChangeAsync should NOT clear Sync storage (contains passkeys)"
        );
    }

    #endregion
}
