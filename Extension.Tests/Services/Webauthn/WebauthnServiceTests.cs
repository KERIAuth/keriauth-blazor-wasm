using Extension.Models;
using Extension.Services;
using Extension.Services.Crypto;
using Extension.Services.JsBindings;
using Extension.Services.Storage;
using FluentResults;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Moq;

namespace Extension.Tests.Services.Webauthn;

/// <summary>
/// Tests for WebauthnService storage operations.
/// Verifies that authenticators are stored in local storage.
/// </summary>
public class WebauthnServiceTests {
    private readonly Mock<IStorageService> _mockStorageService;
    private readonly Mock<INavigatorCredentialsBinding> _mockCredentialsBinding;
    private readonly Mock<ICryptoService> _mockCryptoService;
    private readonly Mock<IFidoMetadataService> _mockFidoMetadataService;
    private readonly Mock<IJSRuntime> _mockJsRuntime;
    private readonly Mock<ILogger<WebauthnService>> _mockLogger;

    public WebauthnServiceTests() {
        _mockStorageService = new Mock<IStorageService>();
        _mockCredentialsBinding = new Mock<INavigatorCredentialsBinding>();
        _mockCryptoService = new Mock<ICryptoService>();
        _mockFidoMetadataService = new Mock<IFidoMetadataService>();
        _mockJsRuntime = new Mock<IJSRuntime>();
        _mockLogger = new Mock<ILogger<WebauthnService>>();
    }

    private WebauthnService CreateService() {
        return new WebauthnService(
            _mockStorageService.Object,
            _mockCredentialsBinding.Object,
            _mockCryptoService.Object,
            _mockFidoMetadataService.Object,
            _mockJsRuntime.Object,
            _mockLogger.Object
        );
    }

    #region GetRegisteredAuthenticatorsAsync Tests

    [Fact]
    public async Task GetRegisteredAuthenticatorsAsync_UsesLocalStorage() {
        // Arrange
        var authenticators = new RegisteredAuthenticators {
            ProfileId = "test-profile-id",
            Authenticators = [
                new RegisteredAuthenticator {
                    SchemaVersion = RegisteredAuthenticatorSchema.CurrentVersion,
                    CredentialBase64 = "test-cred",
                    Transports = ["internal"],
                    EncryptedPasscodeBase64 = "enc",
                    PasscodeHash = 12345,
                    Aaguid = "00000000-0000-0000-0000-000000000000",
                    CreationTime = DateTime.UtcNow
                }
            ]
        };

        _mockStorageService.Setup(s => s.GetItem<RegisteredAuthenticators>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<RegisteredAuthenticators?>(authenticators));

        var service = CreateService();

        // Act
        var result = await service.GetRegisteredAuthenticatorsAsync();

        // Assert
        Assert.True(result.IsSuccess);
        _mockStorageService.Verify(
            s => s.GetItem<RegisteredAuthenticators>(StorageArea.Local),
            Times.Once
        );
    }

    [Fact]
    public async Task GetRegisteredAuthenticatorsAsync_FiltersOldSchemaVersions() {
        // Arrange
        var authenticators = new RegisteredAuthenticators {
            ProfileId = "test-profile-id",
            Authenticators = [
                new RegisteredAuthenticator {
                    SchemaVersion = 1, // Old version - should be filtered
                    CredentialBase64 = "old-cred",
                    Transports = [],
                    EncryptedPasscodeBase64 = "enc",
                    PasscodeHash = 12345,
                    Aaguid = "00000000-0000-0000-0000-000000000000",
                    CreationTime = DateTime.UtcNow
                },
                new RegisteredAuthenticator {
                    SchemaVersion = RegisteredAuthenticatorSchema.CurrentVersion,
                    CredentialBase64 = "new-cred",
                    Transports = ["internal"],
                    EncryptedPasscodeBase64 = "enc",
                    PasscodeHash = 12345,
                    Aaguid = "00000000-0000-0000-0000-000000000000",
                    CreationTime = DateTime.UtcNow
                }
            ]
        };

        _mockStorageService.Setup(s => s.GetItem<RegisteredAuthenticators>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<RegisteredAuthenticators?>(authenticators));

        var service = CreateService();

        // Act
        var result = await service.GetRegisteredAuthenticatorsAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Authenticators);
        Assert.Equal("new-cred", result.Value.Authenticators[0].CredentialBase64);
    }

    [Fact]
    public async Task GetRegisteredAuthenticatorsAsync_ReturnsEmptyList_WhenNoAuthenticators() {
        // Arrange
        _mockStorageService.Setup(s => s.GetItem<RegisteredAuthenticators>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<RegisteredAuthenticators?>(null));

        var service = CreateService();

        // Act
        var result = await service.GetRegisteredAuthenticatorsAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Authenticators);
    }

    #endregion

    #region RemoveAuthenticatorAsync Tests

    [Fact]
    public async Task RemoveAuthenticatorAsync_UsesLocalStorage() {
        // Arrange
        var authenticators = new RegisteredAuthenticators {
            ProfileId = "test-profile-id",
            Authenticators = [
                new RegisteredAuthenticator {
                    SchemaVersion = RegisteredAuthenticatorSchema.CurrentVersion,
                    CredentialBase64 = "cred-to-remove",
                    Transports = ["usb"],
                    EncryptedPasscodeBase64 = "enc",
                    PasscodeHash = 12345,
                    Aaguid = "00000000-0000-0000-0000-000000000000",
                    CreationTime = DateTime.UtcNow
                }
            ]
        };

        _mockStorageService.Setup(s => s.GetItem<RegisteredAuthenticators>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<RegisteredAuthenticators?>(authenticators));
        _mockStorageService.Setup(s => s.SetItem(It.IsAny<RegisteredAuthenticators>(), StorageArea.Local))
            .ReturnsAsync(Result.Ok());

        var service = CreateService();

        // Act
        var result = await service.RemoveAuthenticatorAsync("cred-to-remove");

        // Assert
        Assert.True(result.IsSuccess);
        _mockStorageService.Verify(
            s => s.SetItem(It.IsAny<RegisteredAuthenticators>(), StorageArea.Local),
            Times.Once
        );
    }

    [Fact]
    public async Task RemoveAuthenticatorAsync_RemovesMatchingAuthenticator() {
        // Arrange
        var authenticators = new RegisteredAuthenticators {
            ProfileId = "test-profile-id",
            Authenticators = [
                new RegisteredAuthenticator {
                    SchemaVersion = RegisteredAuthenticatorSchema.CurrentVersion,
                    CredentialBase64 = "cred-to-keep",
                    Transports = ["internal"],
                    EncryptedPasscodeBase64 = "enc",
                    PasscodeHash = 12345,
                    Aaguid = "00000000-0000-0000-0000-000000000000",
                    CreationTime = DateTime.UtcNow
                },
                new RegisteredAuthenticator {
                    SchemaVersion = RegisteredAuthenticatorSchema.CurrentVersion,
                    CredentialBase64 = "cred-to-remove",
                    Transports = ["usb"],
                    EncryptedPasscodeBase64 = "enc",
                    PasscodeHash = 12345,
                    Aaguid = "00000000-0000-0000-0000-000000000000",
                    CreationTime = DateTime.UtcNow
                }
            ]
        };

        _mockStorageService.Setup(s => s.GetItem<RegisteredAuthenticators>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<RegisteredAuthenticators?>(authenticators));
        _mockStorageService.Setup(s => s.SetItem(It.IsAny<RegisteredAuthenticators>(), StorageArea.Local))
            .ReturnsAsync(Result.Ok());

        var service = CreateService();

        // Act
        var result = await service.RemoveAuthenticatorAsync("cred-to-remove");

        // Assert
        Assert.True(result.IsSuccess);
        _mockStorageService.Verify(
            s => s.SetItem(
                It.Is<RegisteredAuthenticators>(ra =>
                    ra.Authenticators.Count == 1 &&
                    ra.Authenticators[0].CredentialBase64 == "cred-to-keep"),
                StorageArea.Local),
            Times.Once
        );
    }

    [Fact]
    public async Task RemoveAuthenticatorAsync_Fails_WhenNotFound() {
        // Arrange
        var authenticators = new RegisteredAuthenticators {
            ProfileId = "test-profile-id",
            Authenticators = [
                new RegisteredAuthenticator {
                    SchemaVersion = RegisteredAuthenticatorSchema.CurrentVersion,
                    CredentialBase64 = "existing-cred",
                    Transports = ["internal"],
                    EncryptedPasscodeBase64 = "enc",
                    PasscodeHash = 12345,
                    Aaguid = "00000000-0000-0000-0000-000000000000",
                    CreationTime = DateTime.UtcNow
                }
            ]
        };

        _mockStorageService.Setup(s => s.GetItem<RegisteredAuthenticators>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<RegisteredAuthenticators?>(authenticators));

        var service = CreateService();

        // Act
        var result = await service.RemoveAuthenticatorAsync("nonexistent-cred");

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("not found", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
