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

    #region ProfileId Computation Tests

    [Fact]
    public async Task AuthenticateAndDecryptPasscodeAsync_FailsWhenKeriaConfigMissing() {
        // Arrange
        var passkeys = new StoredPasskeys {
            ProfileId = null,
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
        _mockStorageService.Setup(s => s.GetItem<KeriaConnectConfig>(StorageArea.Local))
            .ReturnsAsync(Result.Fail<KeriaConnectConfig?>("Config not found"));

        var service = CreateService();

        // Act
        var result = await service.AuthenticateAndDecryptPasscodeAsync();

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("KERIA configuration", result.Errors[0].Message);
    }

    [Fact]
    public async Task AuthenticateAndDecryptPasscodeAsync_FailsWhenClientAidPrefixMissing() {
        // Arrange
        var passkeys = new StoredPasskeys {
            ProfileId = null,
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

        var config = new KeriaConnectConfig(
            providerName: "test",
            adminUrl: "https://test.com",
            bootUrl: "https://boot.test.com",
            passcodeHash: 12345,
            clientAidPrefix: null,  // Missing
            agentAidPrefix: "EAgent123",
            isStored: true
        );

        _mockStorageService.Setup(s => s.GetItem<StoredPasskeys>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<StoredPasskeys?>(passkeys));
        _mockStorageService.Setup(s => s.GetItem<KeriaConnectConfig>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<KeriaConnectConfig?>(config));

        var service = CreateService();

        // Act
        var result = await service.AuthenticateAndDecryptPasscodeAsync();

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("ClientAidPrefix", result.Errors[0].Message);
    }

    [Fact]
    public async Task AuthenticateAndDecryptPasscodeAsync_FailsWhenAgentAidPrefixMissing() {
        // Arrange
        var passkeys = new StoredPasskeys {
            ProfileId = null,
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

        var config = new KeriaConnectConfig(
            providerName: "test",
            adminUrl: "https://test.com",
            bootUrl: "https://boot.test.com",
            passcodeHash: 12345,
            clientAidPrefix: "EClient123",
            agentAidPrefix: null,  // Missing
            isStored: true
        );

        _mockStorageService.Setup(s => s.GetItem<StoredPasskeys>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<StoredPasskeys?>(passkeys));
        _mockStorageService.Setup(s => s.GetItem<KeriaConnectConfig>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<KeriaConnectConfig?>(config));

        var service = CreateService();

        // Act
        var result = await service.AuthenticateAndDecryptPasscodeAsync();

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("AgentAidPrefix", result.Errors[0].Message);
    }

    [Fact]
    public async Task AuthenticateAndDecryptPasscodeAsync_FailsWhenPasscodeHashIsZero() {
        // Arrange
        var passkeys = new StoredPasskeys {
            ProfileId = null,
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

        var config = new KeriaConnectConfig(
            providerName: "test",
            adminUrl: "https://test.com",
            bootUrl: "https://boot.test.com",
            passcodeHash: 0,  // Invalid - zero
            clientAidPrefix: "EClient123",
            agentAidPrefix: "EAgent123",
            isStored: true
        );

        _mockStorageService.Setup(s => s.GetItem<StoredPasskeys>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<StoredPasskeys?>(passkeys));
        _mockStorageService.Setup(s => s.GetItem<KeriaConnectConfig>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<KeriaConnectConfig?>(config));

        var service = CreateService();

        // Act
        var result = await service.AuthenticateAndDecryptPasscodeAsync();

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("PasscodeHash", result.Errors[0].Message);
    }

    #endregion
}
