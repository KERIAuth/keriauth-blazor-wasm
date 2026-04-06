using Extension.Models;
using Extension.Services;
using Extension.Services.Crypto;
using Extension.Services.JsBindings;
using Extension.Services.Port;
using Extension.Services.Storage;
using FluentResults;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Moq;

namespace Extension.Tests.Services.Webauthn;

/// <summary>
/// Tests for WebauthnService storage operations.
/// Passkeys are now stored within KeriaConnectConfig inside KeriaConnectConfigs.
/// </summary>
public class WebauthnServiceTests {
    private readonly Mock<IStorageGateway> _mockStorageGateway;
    private readonly Mock<INavigatorCredentialsBinding> _mockCredentialsBinding;
    private readonly Mock<ICryptoService> _mockCryptoService;
    private readonly Mock<IFidoMetadataService> _mockFidoMetadataService;
    private readonly Mock<IJSRuntime> _mockJsRuntime;
    private readonly Mock<ILogger<WebauthnService>> _mockLogger;
    private readonly Mock<IAppBwPortService> _mockAppBwPortService;

    private const string TestDigest = "test-digest";

    public WebauthnServiceTests() {
        _mockStorageGateway = new Mock<IStorageGateway>();
        _mockCredentialsBinding = new Mock<INavigatorCredentialsBinding>();
        _mockCryptoService = new Mock<ICryptoService>();
        _mockFidoMetadataService = new Mock<IFidoMetadataService>();
        _mockJsRuntime = new Mock<IJSRuntime>();
        _mockLogger = new Mock<ILogger<WebauthnService>>();
        _mockAppBwPortService = new Mock<IAppBwPortService>();
    }

    private WebauthnService CreateService() {
        return new WebauthnService(
            _mockStorageGateway.Object,
            _mockCredentialsBinding.Object,
            _mockCryptoService.Object,
            _mockFidoMetadataService.Object,
            _mockJsRuntime.Object,
            _mockLogger.Object,
            _mockAppBwPortService.Object
        );
    }

    /// <summary>
    /// Sets up mocks for Preferences and KeriaConnectConfigs with the given passkeys.
    /// </summary>
    private void SetupPasskeyMocks(string? digest, List<StoredPasskey> passkeys) {
        var prefs = new Preferences { SelectedKeriaConnectionDigest = digest };
        _mockStorageGateway.Setup(s => s.GetItem<Preferences>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<Preferences?>(prefs));

        if (digest is not null) {
            var config = new KeriaConnectConfig { Passkeys = passkeys };
            var configs = new KeriaConnectConfigs {
                Configs = new Dictionary<string, KeriaConnectConfig> { [digest] = config }
            };
            _mockStorageGateway.Setup(s => s.GetItem<KeriaConnectConfigs>(StorageArea.Local))
                .ReturnsAsync(Result.Ok<KeriaConnectConfigs?>(configs));
            _mockStorageGateway.Setup(s => s.SetItem(It.IsAny<KeriaConnectConfigs>(), StorageArea.Local))
                .ReturnsAsync(Result.Ok());
        }
    }

    private static StoredPasskey MakePasskey(string credId, int schemaVersion = 0) =>
        new() {
            SchemaVersion = schemaVersion == 0 ? StoredPasskeySchema.CurrentVersion : schemaVersion,
            CredentialBase64 = credId,
            Transports = ["internal"],
            EncryptedPasscodeBase64 = "enc",
            KeriaConnectionDigest = TestDigest,
            Aaguid = "00000000-0000-0000-0000-000000000000",
            CreationTime = DateTime.UtcNow
        };

    #region GetStoredPasskeysAsync Tests

    [Fact]
    public async Task GetStoredPasskeysAsync_ReturnsPasskeysFromConfig() {
        // Arrange
        SetupPasskeyMocks(TestDigest, [MakePasskey("test-cred")]);
        var service = CreateService();

        // Act
        var result = await service.GetStoredPasskeysAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("test-cred", result.Value[0].CredentialBase64);
    }

    [Fact]
    public async Task GetStoredPasskeysAsync_FiltersOldSchemaVersions() {
        // Arrange
        SetupPasskeyMocks(TestDigest, [
            MakePasskey("old-cred", schemaVersion: 1),
            MakePasskey("new-cred")
        ]);
        var service = CreateService();

        // Act
        var result = await service.GetStoredPasskeysAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("new-cred", result.Value[0].CredentialBase64);
    }

    [Fact]
    public async Task GetStoredPasskeysAsync_ReturnsEmptyList_WhenNoPasskeys() {
        // Arrange
        SetupPasskeyMocks(TestDigest, []);
        var service = CreateService();

        // Act
        var result = await service.GetStoredPasskeysAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    #endregion

    #region RemovePasskeyAsync Tests

    [Fact]
    public async Task RemovePasskeyAsync_RemovesMatchingPasskey() {
        // Arrange
        SetupPasskeyMocks(TestDigest, [
            MakePasskey("cred-to-keep"),
            MakePasskey("cred-to-remove")
        ]);
        var service = CreateService();

        // Act
        var result = await service.RemovePasskeyAsync("cred-to-remove");

        // Assert
        Assert.True(result.IsSuccess);
        _mockStorageGateway.Verify(
            s => s.SetItem(
                It.Is<KeriaConnectConfigs>(kcc =>
                    kcc.Configs[TestDigest].Passkeys.Count == 1 &&
                    kcc.Configs[TestDigest].Passkeys[0].CredentialBase64 == "cred-to-keep"),
                StorageArea.Local),
            Times.Once
        );
    }

    [Fact]
    public async Task RemovePasskeyAsync_Fails_WhenNotFound() {
        // Arrange
        SetupPasskeyMocks(TestDigest, [MakePasskey("existing-cred")]);
        var service = CreateService();

        // Act
        var result = await service.RemovePasskeyAsync("nonexistent-cred");

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("not found", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region GetCurrentKeriaConnectionDigestAsync Tests

    [Fact]
    public async Task AuthenticateAndDecryptPasscodeAsync_FailsWhenPreferencesNotFound() {
        // Arrange
        _mockStorageGateway.Setup(s => s.GetItem<Preferences>(StorageArea.Local))
            .ReturnsAsync(Result.Fail<Preferences?>("Preferences not found"));

        var service = CreateService();

        // Act
        var result = await service.AuthenticateAndDecryptPasscodeAsync();

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("Preferences", result.Errors[0].Message);
    }

    [Fact]
    public async Task AuthenticateAndDecryptPasscodeAsync_FailsWhenSelectedKeriaConnectionDigestNull() {
        // Arrange
        SetupPasskeyMocks(null, []);

        var service = CreateService();

        // Act
        var result = await service.AuthenticateAndDecryptPasscodeAsync();

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("keria configuration", result.Errors[0].Message.ToLowerInvariant());
    }

    [Fact]
    public async Task AuthenticateAndDecryptPasscodeAsync_FailsWhenSelectedKeriaConnectionDigestEmpty() {
        // Arrange
        var prefs = new Preferences { SelectedKeriaConnectionDigest = "" };
        _mockStorageGateway.Setup(s => s.GetItem<Preferences>(StorageArea.Local))
            .ReturnsAsync(Result.Ok<Preferences?>(prefs));

        var service = CreateService();

        // Act
        var result = await service.AuthenticateAndDecryptPasscodeAsync();

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("keria configuration", result.Errors[0].Message.ToLowerInvariant());
    }

    [Fact]
    public async Task AuthenticateAndDecryptPasscodeAsync_FailsWhenNoStoredPasskeys() {
        // Arrange
        SetupPasskeyMocks(TestDigest, []);

        var service = CreateService();

        // Act
        var result = await service.AuthenticateAndDecryptPasscodeAsync();

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("passkey", result.Errors[0].Message.ToLowerInvariant());
    }

    #endregion
}
