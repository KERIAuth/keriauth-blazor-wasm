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
/// Verifies that passkeys are stored in local storage.
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

    #region GetStoredPasskeysAsync Tests

    [Fact]
    public async Task GetStoredPasskeysAsync_UsesLocalStorage() {
        // Arrange
        var passkeys = new StoredPasskeys {
            ProfileId = "test-profile-id",
            Passkeys = [
                new StoredPasskey {
                    SchemaVersion = StoredPasskeySchema.CurrentVersion,
                    CredentialBase64 = "test-cred",
                    Transports = ["internal"],
                    EncryptedPasscodeBase64 = "enc",
                    PasscodeHash = 12345,
                    Aaguid = "00000000-0000-0000-0000-000000000000",
                    CreationTime = DateTime.UtcNow
                }
            ]
        };

        _mockStorageService.Setup(s => s.GetItem<StoredPasskeys>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<StoredPasskeys?>(passkeys));

        var service = CreateService();

        // Act
        var result = await service.GetStoredPasskeysAsync();

        // Assert
        Assert.True(result.IsSuccess);
        _mockStorageService.Verify(
            s => s.GetItem<StoredPasskeys>(StorageArea.Local),
            Times.Once
        );
    }

    [Fact]
    public async Task GetStoredPasskeysAsync_FiltersOldSchemaVersions() {
        // Arrange
        var passkeys = new StoredPasskeys {
            ProfileId = "test-profile-id",
            Passkeys = [
                new StoredPasskey {
                    SchemaVersion = 1, // Old version - should be filtered
                    CredentialBase64 = "old-cred",
                    Transports = [],
                    EncryptedPasscodeBase64 = "enc",
                    PasscodeHash = 12345,
                    Aaguid = "00000000-0000-0000-0000-000000000000",
                    CreationTime = DateTime.UtcNow
                },
                new StoredPasskey {
                    SchemaVersion = StoredPasskeySchema.CurrentVersion,
                    CredentialBase64 = "new-cred",
                    Transports = ["internal"],
                    EncryptedPasscodeBase64 = "enc",
                    PasscodeHash = 12345,
                    Aaguid = "00000000-0000-0000-0000-000000000000",
                    CreationTime = DateTime.UtcNow
                }
            ]
        };

        _mockStorageService.Setup(s => s.GetItem<StoredPasskeys>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<StoredPasskeys?>(passkeys));

        var service = CreateService();

        // Act
        var result = await service.GetStoredPasskeysAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Passkeys);
        Assert.Equal("new-cred", result.Value.Passkeys[0].CredentialBase64);
    }

    [Fact]
    public async Task GetStoredPasskeysAsync_ReturnsEmptyList_WhenNoPasskeys() {
        // Arrange
        _mockStorageService.Setup(s => s.GetItem<StoredPasskeys>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<StoredPasskeys?>(null));

        var service = CreateService();

        // Act
        var result = await service.GetStoredPasskeysAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Passkeys);
    }

    #endregion

    #region RemovePasskeyAsync Tests

    [Fact]
    public async Task RemovePasskeyAsync_UsesLocalStorage() {
        // Arrange
        var passkeys = new StoredPasskeys {
            ProfileId = "test-profile-id",
            Passkeys = [
                new StoredPasskey {
                    SchemaVersion = StoredPasskeySchema.CurrentVersion,
                    CredentialBase64 = "cred-to-remove",
                    Transports = ["usb"],
                    EncryptedPasscodeBase64 = "enc",
                    PasscodeHash = 12345,
                    Aaguid = "00000000-0000-0000-0000-000000000000",
                    CreationTime = DateTime.UtcNow
                }
            ]
        };

        _mockStorageService.Setup(s => s.GetItem<StoredPasskeys>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<StoredPasskeys?>(passkeys));
        _mockStorageService.Setup(s => s.SetItem(It.IsAny<StoredPasskeys>(), StorageArea.Local))
            .ReturnsAsync(Result.Ok());

        var service = CreateService();

        // Act
        var result = await service.RemovePasskeyAsync("cred-to-remove");

        // Assert
        Assert.True(result.IsSuccess);
        _mockStorageService.Verify(
            s => s.SetItem(It.IsAny<StoredPasskeys>(), StorageArea.Local),
            Times.Once
        );
    }

    [Fact]
    public async Task RemovePasskeyAsync_RemovesMatchingPasskey() {
        // Arrange
        var passkeys = new StoredPasskeys {
            ProfileId = "test-profile-id",
            Passkeys = [
                new StoredPasskey {
                    SchemaVersion = StoredPasskeySchema.CurrentVersion,
                    CredentialBase64 = "cred-to-keep",
                    Transports = ["internal"],
                    EncryptedPasscodeBase64 = "enc",
                    PasscodeHash = 12345,
                    Aaguid = "00000000-0000-0000-0000-000000000000",
                    CreationTime = DateTime.UtcNow
                },
                new StoredPasskey {
                    SchemaVersion = StoredPasskeySchema.CurrentVersion,
                    CredentialBase64 = "cred-to-remove",
                    Transports = ["usb"],
                    EncryptedPasscodeBase64 = "enc",
                    PasscodeHash = 12345,
                    Aaguid = "00000000-0000-0000-0000-000000000000",
                    CreationTime = DateTime.UtcNow
                }
            ]
        };

        _mockStorageService.Setup(s => s.GetItem<StoredPasskeys>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<StoredPasskeys?>(passkeys));
        _mockStorageService.Setup(s => s.SetItem(It.IsAny<StoredPasskeys>(), StorageArea.Local))
            .ReturnsAsync(Result.Ok());

        var service = CreateService();

        // Act
        var result = await service.RemovePasskeyAsync("cred-to-remove");

        // Assert
        Assert.True(result.IsSuccess);
        _mockStorageService.Verify(
            s => s.SetItem(
                It.Is<StoredPasskeys>(sp =>
                    sp.Passkeys.Count == 1 &&
                    sp.Passkeys[0].CredentialBase64 == "cred-to-keep"),
                StorageArea.Local),
            Times.Once
        );
    }

    [Fact]
    public async Task RemovePasskeyAsync_Fails_WhenNotFound() {
        // Arrange
        var passkeys = new StoredPasskeys {
            ProfileId = "test-profile-id",
            Passkeys = [
                new StoredPasskey {
                    SchemaVersion = StoredPasskeySchema.CurrentVersion,
                    CredentialBase64 = "existing-cred",
                    Transports = ["internal"],
                    EncryptedPasscodeBase64 = "enc",
                    PasscodeHash = 12345,
                    Aaguid = "00000000-0000-0000-0000-000000000000",
                    CreationTime = DateTime.UtcNow
                }
            ]
        };

        _mockStorageService.Setup(s => s.GetItem<StoredPasskeys>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<StoredPasskeys?>(passkeys));

        var service = CreateService();

        // Act
        var result = await service.RemovePasskeyAsync("nonexistent-cred");

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("not found", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
