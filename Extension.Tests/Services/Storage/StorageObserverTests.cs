namespace Extension.Tests.Services.Storage;

using Extension.Models;
using Extension.Models.Storage;
using Extension.Services;
using Extension.Services.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for StorageObserver generic observer implementation.
/// Tests auto-subscription, callback invocation, error handling, and disposal.
/// </summary>
public class StorageObserverTests {
    private readonly Mock<IStorageService> _mockStorageService;
    private readonly Mock<ILogger> _mockLogger;

    public StorageObserverTests() {
        _mockStorageService = new Mock<IStorageService>();
        _mockLogger = new Mock<ILogger>();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_SubscribesToStorageService() {
        // Arrange
        Action<Preferences> onNext = _ => { };

        // Act
        var observer = new StorageObserver<Preferences>(
            _mockStorageService.Object,
            StorageArea.Local,
            onNext
        );

        // Assert
        _mockStorageService.Verify(
            s => s.Subscribe<Preferences>(observer, StorageArea.Local),
            Times.Once
        );
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenStorageServiceIsNull() {
        // Arrange
        Action<Preferences> onNext = _ => { };

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new StorageObserver<Preferences>(null!, StorageArea.Local, onNext)
        );
        Assert.Equal("storageService", exception.ParamName);
    }

    #endregion

    #region OnNext Tests

    [Fact]
    public void OnNext_InvokesCallback_WithCorrectValue() {
        // Arrange
        Preferences? receivedPrefs = null;
        var testPrefs = new Preferences { IsDarkTheme = true };

        var observer = new StorageObserver<Preferences>(
            _mockStorageService.Object,
            StorageArea.Local,
            prefs => receivedPrefs = prefs
        );

        // Act
        observer.OnNext(testPrefs);

        // Assert
        Assert.NotNull(receivedPrefs);
        Assert.True(receivedPrefs.IsDarkTheme);
    }

    [Fact]
    public void OnNext_InvokesCallback_MultipleValues() {
        // Arrange
        var callCount = 0;
        var observer = new StorageObserver<Preferences>(
            _mockStorageService.Object,
            StorageArea.Local,
            _ => callCount++
        );

        // Act
        observer.OnNext(new Preferences());
        observer.OnNext(new Preferences { IsDarkTheme = true });
        observer.OnNext(new Preferences { IsDarkTheme = false });

        // Assert
        Assert.Equal(3, callCount);
    }

    [Fact]
    public void OnNext_CallsOnError_WhenCallbackThrows() {
        // Arrange
        Exception? receivedError = null;
        var testException = new InvalidOperationException("Test error");

        var observer = new StorageObserver<Preferences>(
            _mockStorageService.Object,
            StorageArea.Local,
            onNext: _ => throw testException,
            onError: ex => receivedError = ex
        );

        // Act
        observer.OnNext(new Preferences());

        // Assert
        Assert.NotNull(receivedError);
        Assert.Same(testException, receivedError);
    }

    [Fact]
    public void OnNext_WorksWithDifferentStorageModels() {
        // Arrange - Test with PasscodeModel
        PasscodeModel? receivedModel = null;
        var testModel = new PasscodeModel { Passcode = "test123" };

        var observer = new StorageObserver<PasscodeModel>(
            _mockStorageService.Object,
            StorageArea.Session,
            model => receivedModel = model
        );

        // Act
        observer.OnNext(testModel);

        // Assert
        Assert.NotNull(receivedModel);
        Assert.Equal("test123", receivedModel.Passcode);
    }

    #endregion

    #region OnError Tests

    [Fact]
    public void OnError_InvokesErrorHandler_WhenProvided() {
        // Arrange
        Exception? receivedError = null;
        var testException = new InvalidOperationException("Test error");

        var observer = new StorageObserver<Preferences>(
            _mockStorageService.Object,
            StorageArea.Local,
            onNext: _ => { },
            onError: ex => receivedError = ex
        );

        // Act
        observer.OnError(testException);

        // Assert
        Assert.NotNull(receivedError);
        Assert.Same(testException, receivedError);
    }

    [Fact]
    public void OnError_DoesNotThrow_WhenErrorHandlerNotProvided() {
        // Arrange
        var observer = new StorageObserver<Preferences>(
            _mockStorageService.Object,
            StorageArea.Local,
            _ => { }
        );

        // Act & Assert - Should not throw
        var exception = Record.Exception(() =>
            observer.OnError(new InvalidOperationException("Test"))
        );
        Assert.Null(exception);
    }

    #endregion

    #region OnCompleted Tests

    [Fact]
    public void OnCompleted_InvokesCompletedHandler_WhenProvided() {
        // Arrange
        var completedCalled = false;

        var observer = new StorageObserver<Preferences>(
            _mockStorageService.Object,
            StorageArea.Local,
            onNext: _ => { },
            onCompleted: () => completedCalled = true
        );

        // Act
        observer.OnCompleted();

        // Assert
        Assert.True(completedCalled);
    }

    [Fact]
    public void OnCompleted_DoesNotThrow_WhenCompletedHandlerNotProvided() {
        // Arrange
        var observer = new StorageObserver<Preferences>(
            _mockStorageService.Object,
            StorageArea.Local,
            _ => { }
        );

        // Act & Assert - Should not throw
        var exception = Record.Exception(() => observer.OnCompleted());
        Assert.Null(exception);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_DisposesSubscription() {
        // Arrange
        var mockSubscription = new Mock<IDisposable>();
        _mockStorageService
            .Setup(s => s.Subscribe<Preferences>(It.IsAny<IObserver<Preferences>>(), It.IsAny<StorageArea>()))
            .Returns(mockSubscription.Object);

        var observer = new StorageObserver<Preferences>(
            _mockStorageService.Object,
            StorageArea.Local,
            _ => { }
        );

        // Act
        observer.Dispose();

        // Assert
        mockSubscription.Verify(s => s.Dispose(), Times.Once);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes() {
        // Arrange
        var mockSubscription = new Mock<IDisposable>();
        _mockStorageService
            .Setup(s => s.Subscribe<Preferences>(It.IsAny<IObserver<Preferences>>(), It.IsAny<StorageArea>()))
            .Returns(mockSubscription.Object);

        var observer = new StorageObserver<Preferences>(
            _mockStorageService.Object,
            StorageArea.Local,
            _ => { }
        );

        // Act
        observer.Dispose();
        observer.Dispose();
        observer.Dispose();

        // Assert - Should only dispose once
        mockSubscription.Verify(s => s.Dispose(), Times.Once);
    }

    #endregion

    #region Storage Area Tests

    [Theory]
    [InlineData(StorageArea.Local)]
    [InlineData(StorageArea.Session)]
    [InlineData(StorageArea.Sync)]
    [InlineData(StorageArea.Managed)]
    public void Constructor_SubscribesToCorrectStorageArea(StorageArea area) {
        // Act
        var observer = new StorageObserver<Preferences>(
            _mockStorageService.Object,
            area,
            _ => { }
        );

        // Assert
        _mockStorageService.Verify(
            s => s.Subscribe<Preferences>(observer, area),
            Times.Once
        );
    }

    #endregion

    #region Extension Method Tests

    [Fact]
    public void CreateObserver_ExtensionMethod_CreatesObserver() {
        // Act
        var observer = _mockStorageService.Object.CreateObserver<Preferences>(
            StorageArea.Local,
            _ => { }
        );

        // Assert
        Assert.NotNull(observer);
        Assert.IsType<StorageObserver<Preferences>>(observer);
    }

    [Fact]
    public void CreateObserver_ExtensionMethod_WorksWithAllParameters() {
        // Arrange
        var onNextCalled = false;
        var onErrorCalled = false;
        var onCompletedCalled = false;

        // Act
        var observer = _mockStorageService.Object.CreateObserver<Preferences>(
            StorageArea.Local,
            onNext: _ => onNextCalled = true,
            onError: _ => onErrorCalled = true,
            onCompleted: () => onCompletedCalled = true,
            logger: _mockLogger.Object
        );

        // Assert
        Assert.NotNull(observer);
        observer.OnNext(new Preferences());
        observer.OnError(new InvalidOperationException("Test error"));
        observer.OnCompleted();

        Assert.True(onNextCalled);
        Assert.True(onErrorCalled);
        Assert.True(onCompletedCalled);
    }

    #endregion

    #region Integration-style Tests

    [Fact]
    public void StorageObserver_WorksWithAppState() {
        // Arrange
        AppState? receivedState = null;
        var testState = new AppState(IStateService.States.Unauthenticated);

        var observer = new StorageObserver<AppState>(
            _mockStorageService.Object,
            StorageArea.Session,
            state => receivedState = state
        );

        // Act
        observer.OnNext(testState);

        // Assert
        Assert.NotNull(receivedState);
        Assert.Equal(IStateService.States.Unauthenticated, receivedState.CurrentState);
    }

    [Fact]
    public void StorageObserver_WorksWithEnterprisePolicyConfig() {
        // Arrange
        EnterprisePolicyConfig? receivedConfig = null;
        var testConfig = new EnterprisePolicyConfig {
            KeriaAdminUrl = "https://keria.company.com/admin"
        };

        var observer = new StorageObserver<EnterprisePolicyConfig>(
            _mockStorageService.Object,
            StorageArea.Managed,
            config => receivedConfig = config
        );

        // Act
        observer.OnNext(testConfig);

        // Assert
        Assert.NotNull(receivedConfig);
        Assert.Equal("https://keria.company.com/admin", receivedConfig.KeriaAdminUrl);
    }

    #endregion
}
