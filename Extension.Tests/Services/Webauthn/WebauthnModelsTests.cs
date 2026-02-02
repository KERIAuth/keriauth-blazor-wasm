using System.Text.Json;
using Extension.Models;
using Extension.Services.JsBindings;

namespace Extension.Tests.Services.Webauthn;

/// <summary>
/// Tests for WebAuthn-related models and serialization.
/// No mocking - tests actual JSON serialization behavior.
/// </summary>
public class WebauthnModelsTests {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    #region StoredPasskeySchema Tests

    [Fact]
    public void StoredPasskeySchema_CurrentVersion_IsSeven() {
        // Assert - Version 7 renamed ProfileId to KeriaConnectionDigest and removed PasscodeHash
        Assert.Equal(7, StoredPasskeySchema.CurrentVersion);
    }

    #endregion

    #region StoredPasskey Serialization Tests

    [Fact]
    public void StoredPasskey_Serialization_RoundTrip() {
        // Arrange
        var now = DateTime.UtcNow;
        var original = new StoredPasskey {
            SchemaVersion = StoredPasskeySchema.CurrentVersion,
            Name = "Test Passkey",
            CredentialBase64 = "dGVzdC1jcmVkZW50aWFs",
            Transports = ["usb", "internal"],
            EncryptedPasscodeBase64 = "ZW5jcnlwdGVkLXBhc3Njb2Rl",
            KeriaConnectionDigest = "test-keria-connection-digest",
            Aaguid = "00000000-0000-0000-0000-000000000000",
            CreationTime = now,
            LastUpdatedUtc = now
        };

        // Act
        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<StoredPasskey>(json, JsonOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.SchemaVersion, deserialized.SchemaVersion);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.CredentialBase64, deserialized.CredentialBase64);
        Assert.Equal(original.Transports, deserialized.Transports);
        Assert.Equal(original.EncryptedPasscodeBase64, deserialized.EncryptedPasscodeBase64);
    }

    [Fact]
    public void StoredPasskey_Serialization_UsesCorrectPropertyNames() {
        // Arrange
        var passkey = new StoredPasskey {
            SchemaVersion = StoredPasskeySchema.CurrentVersion,
            Name = "Test",
            CredentialBase64 = "abc123",
            Transports = ["internal"],
            EncryptedPasscodeBase64 = "encrypted",
            KeriaConnectionDigest = "test-keria-connection-digest",
            Aaguid = "00000000-0000-0000-0000-000000000000",
            CreationTime = DateTime.UtcNow,
            LastUpdatedUtc = DateTime.UtcNow
        };

        // Act
        var json = JsonSerializer.Serialize(passkey, JsonOptions);

        // Assert - Check JSON property names match JsonPropertyName attributes (PascalCase)
        Assert.Contains("\"SchemaVersion\":", json);
        Assert.Contains("\"Name\":", json);
        Assert.Contains("\"CredentialBase64\":", json);
        Assert.Contains("\"Transports\":", json);
        Assert.Contains("\"EncryptedPasscodeBase64\":", json);
        Assert.Contains("\"CreationTime\":", json);
        Assert.Contains("\"LastUpdatedUtc\":", json);
    }

    [Fact]
    public void StoredPasskey_Transports_CanBeEmpty() {
        // Arrange
        var passkey = new StoredPasskey {
            SchemaVersion = StoredPasskeySchema.CurrentVersion,
            CredentialBase64 = "abc",
            Transports = [],
            EncryptedPasscodeBase64 = "enc",
            KeriaConnectionDigest = "test-keria-connection-digest",
            Aaguid = "00000000-0000-0000-0000-000000000000",
            CreationTime = DateTime.UtcNow
        };

        // Act
        var json = JsonSerializer.Serialize(passkey, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<StoredPasskey>(json, JsonOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.Transports);
    }

    [Fact]
    public void StoredPasskey_Transports_PreservesOrder() {
        // Arrange
        var transports = new[] { "usb", "nfc", "ble", "internal", "hybrid" };
        var passkey = new StoredPasskey {
            SchemaVersion = StoredPasskeySchema.CurrentVersion,
            CredentialBase64 = "abc",
            Transports = transports,
            EncryptedPasscodeBase64 = "enc",
            KeriaConnectionDigest = "test-keria-connection-digest",
            Aaguid = "00000000-0000-0000-0000-000000000000",
            CreationTime = DateTime.UtcNow
        };

        // Act
        var json = JsonSerializer.Serialize(passkey, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<StoredPasskey>(json, JsonOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(transports, deserialized.Transports);
    }

    [Fact]
    public void StoredPasskey_Name_CanBeNull() {
        // Arrange
        var passkey = new StoredPasskey {
            SchemaVersion = StoredPasskeySchema.CurrentVersion,
            Name = null,
            CredentialBase64 = "abc",
            Transports = ["internal"],
            EncryptedPasscodeBase64 = "enc",
            KeriaConnectionDigest = "test-keria-connection-digest",
            Aaguid = "00000000-0000-0000-0000-000000000000",
            CreationTime = DateTime.UtcNow
        };

        // Act
        var json = JsonSerializer.Serialize(passkey, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<StoredPasskey>(json, JsonOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Name);
    }

    #endregion

    #region CreateCredentialOptions Serialization Tests

    [Fact]
    public void CreateCredentialOptions_Serialization_RoundTrip() {
        // Arrange
        var original = new CreateCredentialOptions {
            ExcludeCredentialIds = ["cred1", "cred2"],
            ResidentKey = "required",
            AuthenticatorAttachment = "platform",
            UserVerification = "required",
            Attestation = "none",
            Hints = ["client-device"],
            UserIdBase64 = "dXNlci1pZA==",
            UserName = "KERI Auth (123456)",
            PrfSaltBase64 = "c2FsdC12YWx1ZQ=="
        };

        // Act
        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<CreateCredentialOptions>(json, JsonOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.ExcludeCredentialIds, deserialized.ExcludeCredentialIds);
        Assert.Equal(original.ResidentKey, deserialized.ResidentKey);
        Assert.Equal(original.AuthenticatorAttachment, deserialized.AuthenticatorAttachment);
        Assert.Equal(original.UserVerification, deserialized.UserVerification);
        Assert.Equal(original.Attestation, deserialized.Attestation);
        Assert.Equal(original.Hints, deserialized.Hints);
        Assert.Equal(original.UserIdBase64, deserialized.UserIdBase64);
        Assert.Equal(original.UserName, deserialized.UserName);
        Assert.Equal(original.PrfSaltBase64, deserialized.PrfSaltBase64);
    }

    [Fact]
    public void CreateCredentialOptions_AuthenticatorAttachment_CanBeNull() {
        // Arrange
        var options = new CreateCredentialOptions {
            ExcludeCredentialIds = [],
            ResidentKey = "preferred",
            AuthenticatorAttachment = null,
            UserVerification = "preferred",
            Attestation = "none",
            Hints = [],
            UserIdBase64 = "dXNlcg==",
            UserName = "Test User",
            PrfSaltBase64 = "c2FsdA=="
        };

        // Act
        var json = JsonSerializer.Serialize(options, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<CreateCredentialOptions>(json, JsonOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Null(deserialized.AuthenticatorAttachment);
    }

    [Fact]
    public void CreateCredentialOptions_UsesCorrectPropertyNames() {
        // Arrange
        var options = new CreateCredentialOptions {
            ExcludeCredentialIds = [],
            ResidentKey = "required",
            UserVerification = "required",
            Attestation = "none",
            Hints = [],
            UserIdBase64 = "dXNlcg==",
            UserName = "Test",
            PrfSaltBase64 = "c2FsdA=="
        };

        // Act
        var json = JsonSerializer.Serialize(options, JsonOptions);

        // Assert
        Assert.Contains("\"excludeCredentialIds\":", json);
        Assert.Contains("\"residentKey\":", json);
        Assert.Contains("\"userVerification\":", json);
        Assert.Contains("\"attestation\":", json);
        Assert.Contains("\"hints\":", json);
        Assert.Contains("\"userIdBase64\":", json);
        Assert.Contains("\"userName\":", json);
        Assert.Contains("\"prfSaltBase64\":", json);
    }

    #endregion

    #region GetCredentialOptions Serialization Tests

    [Fact]
    public void GetCredentialOptions_Serialization_RoundTrip() {
        // Arrange
        var original = new GetCredentialOptions {
            AllowCredentialIds = ["cred1", "cred2"],
            TransportsPerCredential = [["usb", "internal"], ["nfc"]],
            UserVerification = "preferred",
            PrfSaltBase64 = "c2FsdC12YWx1ZQ=="
        };

        // Act
        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GetCredentialOptions>(json, JsonOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.AllowCredentialIds, deserialized.AllowCredentialIds);
        Assert.Equal(original.TransportsPerCredential.Count, deserialized.TransportsPerCredential.Count);
        Assert.Equal(original.TransportsPerCredential[0], deserialized.TransportsPerCredential[0]);
        Assert.Equal(original.TransportsPerCredential[1], deserialized.TransportsPerCredential[1]);
        Assert.Equal(original.UserVerification, deserialized.UserVerification);
        Assert.Equal(original.PrfSaltBase64, deserialized.PrfSaltBase64);
    }

    [Fact]
    public void GetCredentialOptions_UsesCorrectPropertyNames() {
        // Arrange
        var options = new GetCredentialOptions {
            AllowCredentialIds = ["cred1"],
            TransportsPerCredential = [["internal"]],
            UserVerification = "required",
            PrfSaltBase64 = "c2FsdA=="
        };

        // Act
        var json = JsonSerializer.Serialize(options, JsonOptions);

        // Assert
        Assert.Contains("\"allowCredentialIds\":", json);
        Assert.Contains("\"transportsPerCredential\":", json);
        Assert.Contains("\"userVerification\":", json);
        Assert.Contains("\"prfSaltBase64\":", json);
    }

    #endregion

    #region CredentialCreationResult Serialization Tests

    [Fact]
    public void CredentialCreationResult_Deserialization_FromTypicalJsResponse() {
        // Arrange - Simulates JSON response from TypeScript shim
        var json = """
        {
            "credentialId": "dGVzdC1jcmVkLWlk",
            "transports": ["internal", "hybrid"],
            "prfEnabled": true,
            "residentKeyCreated": true,
            "aaguid": "08987058-cadc-4b81-b6e1-30de50dcbe96"
        }
        """;

        // Act
        var result = JsonSerializer.Deserialize<CredentialCreationResult>(json, JsonOptions);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("dGVzdC1jcmVkLWlk", result.CredentialId);
        Assert.Equal(["internal", "hybrid"], result.Transports);
        Assert.True(result.PrfEnabled);
        Assert.True(result.ResidentKeyCreated);
        Assert.Equal("08987058-cadc-4b81-b6e1-30de50dcbe96", result.Aaguid);
    }

    [Fact]
    public void CredentialCreationResult_Deserialization_PrfDisabled() {
        // Arrange
        var json = """
        {
            "credentialId": "abc",
            "transports": ["usb"],
            "prfEnabled": false,
            "residentKeyCreated": true,
            "aaguid": "00000000-0000-0000-0000-000000000000"
        }
        """;

        // Act
        var result = JsonSerializer.Deserialize<CredentialCreationResult>(json, JsonOptions);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.PrfEnabled);
    }

    #endregion

    #region CredentialAssertionResult Serialization Tests

    [Fact]
    public void CredentialAssertionResult_Deserialization_WithPrfOutput() {
        // Arrange - Simulates JSON response from TypeScript shim
        var json = """
        {
            "credentialId": "dGVzdC1jcmVkLWlk",
            "prfOutputBase64": "MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDEy"
        }
        """;

        // Act
        var result = JsonSerializer.Deserialize<CredentialAssertionResult>(json, JsonOptions);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("dGVzdC1jcmVkLWlk", result.CredentialId);
        Assert.Equal("MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDEy", result.PrfOutputBase64);
    }

    [Fact]
    public void CredentialAssertionResult_Deserialization_NullPrfOutput() {
        // Arrange
        var json = """
        {
            "credentialId": "abc",
            "prfOutputBase64": null
        }
        """;

        // Act
        var result = JsonSerializer.Deserialize<CredentialAssertionResult>(json, JsonOptions);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("abc", result.CredentialId);
        Assert.Null(result.PrfOutputBase64);
    }

    #endregion

    #region StoredPasskeys Collection Tests

    [Fact]
    public void StoredPasskeys_Serialization_RoundTrip() {
        // Arrange
        var now = DateTime.UtcNow;
        var original = new StoredPasskeys {
            Passkeys = [
                new StoredPasskey {
                    SchemaVersion = StoredPasskeySchema.CurrentVersion,
                    Name = "Passkey 1",
                    CredentialBase64 = "cred1",
                    Transports = ["internal"],
                    EncryptedPasscodeBase64 = "enc1",
                    KeriaConnectionDigest = "test-keria-connection-digest",
                    Aaguid = "00000000-0000-0000-0000-000000000000",
                    CreationTime = now
                },
                new StoredPasskey {
                    SchemaVersion = StoredPasskeySchema.CurrentVersion,
                    Name = "Passkey 2",
                    CredentialBase64 = "cred2",
                    Transports = ["usb", "nfc"],
                    EncryptedPasscodeBase64 = "enc2",
                    KeriaConnectionDigest = "test-keria-connection-digest",
                    Aaguid = "00000000-0000-0000-0000-000000000000",
                    CreationTime = now
                }
            ]
        };

        // Act
        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<StoredPasskeys>(json, JsonOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Passkeys.Count);
        Assert.Equal("Passkey 1", deserialized.Passkeys[0].Name);
        Assert.Equal("Passkey 2", deserialized.Passkeys[1].Name);
    }

    [Fact]
    public void StoredPasskeys_Empty_SerializesCorrectly() {
        // Arrange
        var empty = new StoredPasskeys { Passkeys = [] };

        // Act
        var json = JsonSerializer.Serialize(empty, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<StoredPasskeys>(json, JsonOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.Passkeys);
    }

    #endregion

    #region Schema Version Filtering Logic Tests

    [Fact]
    public void SchemaVersionFiltering_CurrentVersion_IsValid() {
        // Arrange
        var passkey = new StoredPasskey {
            SchemaVersion = StoredPasskeySchema.CurrentVersion,
            CredentialBase64 = "cred",
            Transports = ["internal"],
            EncryptedPasscodeBase64 = "enc",
            KeriaConnectionDigest = "test-keria-connection-digest",
            Aaguid = "00000000-0000-0000-0000-000000000000",
            CreationTime = DateTime.UtcNow
        };

        // Act
        var isValid = passkey.SchemaVersion == StoredPasskeySchema.CurrentVersion;

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void SchemaVersionFiltering_OldVersion_IsInvalid() {
        // Arrange - Version 1 is the old format without Transports
        var passkey = new StoredPasskey {
            SchemaVersion = 1,
            CredentialBase64 = "cred",
            Transports = [],  // Would be missing in actual old data
            EncryptedPasscodeBase64 = "enc",
            KeriaConnectionDigest = "test-keria-connection-digest",
            Aaguid = "00000000-0000-0000-0000-000000000000",
            CreationTime = DateTime.UtcNow
        };

        // Act
        var isValid = passkey.SchemaVersion == StoredPasskeySchema.CurrentVersion;

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void SchemaVersionFiltering_FutureVersion_IsInvalid() {
        // Arrange - A hypothetical future version
        var passkey = new StoredPasskey {
            SchemaVersion = 99,
            CredentialBase64 = "cred",
            Transports = ["internal"],
            EncryptedPasscodeBase64 = "enc",
            KeriaConnectionDigest = "test-keria-connection-digest",
            Aaguid = "00000000-0000-0000-0000-000000000000",
            CreationTime = DateTime.UtcNow
        };

        // Act
        var isValid = passkey.SchemaVersion == StoredPasskeySchema.CurrentVersion;

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void SchemaVersionFiltering_MixedVersions_FiltersCorrectly() {
        // Arrange - Mix of old and current versions
        var passkeys = new List<StoredPasskey> {
            new() {
                SchemaVersion = 1,  // Old
                CredentialBase64 = "old-cred",
                Transports = [],
                EncryptedPasscodeBase64 = "old-enc",
                KeriaConnectionDigest = "test-keria-connection-digest",
                Aaguid = "00000000-0000-0000-0000-000000000000",
                CreationTime = DateTime.UtcNow
            },
            new() {
                SchemaVersion = StoredPasskeySchema.CurrentVersion,
                CredentialBase64 = "new-cred",
                Transports = ["internal"],
                EncryptedPasscodeBase64 = "new-enc",
                KeriaConnectionDigest = "test-keria-connection-digest",
                Aaguid = "00000000-0000-0000-0000-000000000000",
                CreationTime = DateTime.UtcNow
            },
            new() {
                SchemaVersion = StoredPasskeySchema.CurrentVersion,
                CredentialBase64 = "another-cred",
                Transports = ["usb"],
                EncryptedPasscodeBase64 = "another-enc",
                KeriaConnectionDigest = "test-keria-connection-digest",
                Aaguid = "00000000-0000-0000-0000-000000000000",
                CreationTime = DateTime.UtcNow
            }
        };

        // Act - Same filtering logic as WebauthnService.GetValidPasskeysAsync
        var valid = passkeys
            .Where(a => a.SchemaVersion == StoredPasskeySchema.CurrentVersion)
            .ToList();

        // Assert
        Assert.Equal(2, valid.Count);
        Assert.All(valid, a => Assert.Equal(StoredPasskeySchema.CurrentVersion, a.SchemaVersion));
        Assert.DoesNotContain(valid, a => a.CredentialBase64 == "old-cred");
    }

    #endregion
}
