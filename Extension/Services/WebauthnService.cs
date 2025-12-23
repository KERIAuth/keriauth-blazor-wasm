using FluentResults;
using Extension.Models;
using Extension.Models.Storage;
using Extension.Services.Crypto;
using Extension.Services.JsBindings;
using Extension.Services.Storage;
using Microsoft.JSInterop;
using System.Text;

namespace Extension.Services;

/// <summary>
/// WebAuthn service for passkey creation and authentication using PRF extension.
/// Uses C# for cryptography (SHA-256, AES-GCM) and minimal TypeScript shim for navigator.credentials API.
/// </summary>
public class WebauthnService : IWebauthnService {
    private readonly IStorageService _storageService;
    private readonly INavigatorCredentialsBinding _credentialsBinding;
    private readonly ICryptoService _cryptoService;
    private readonly IFidoMetadataService _fidoMetadataService;
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<WebauthnService> _logger;

    // Fixed nonce for AES-GCM encryption.
    // This is safe because keys are derived fresh from PRF each time (never reused).
    // The PRF output is unique per-authenticator and per-profile, so the key is always unique.
    private static readonly byte[] FixedNonce = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];

    private const string KeriAuthExtensionName = "KERI Auth";

    public WebauthnService(
        IStorageService storageService,
        INavigatorCredentialsBinding credentialsBinding,
        ICryptoService cryptoService,
        IFidoMetadataService fidoMetadataService,
        IJSRuntime jsRuntime,
        ILogger<WebauthnService> logger) {
        _storageService = storageService;
        _credentialsBinding = credentialsBinding;
        _cryptoService = cryptoService;
        _fidoMetadataService = fidoMetadataService;
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public async Task<Result<string>> RegisterAttestStoreAuthenticatorAsync(
        string residentKey,
        string? authenticatorAttachment,
        string userVerification,
        string attestationConveyancePreference,
        List<string> hints) {
        try {
            // Get profile identifier and compute PRF salt
            var profileId = await GetOrCreateProfileIdentifierAsync();
            var prfSalt = ComputePrfSalt(profileId);
            var prfSaltBase64 = Convert.ToBase64String(prfSalt);

            // Compute user ID from profile identifier
            var userId = _cryptoService.Sha256(Encoding.UTF8.GetBytes(profileId));
            var userIdBase64 = Convert.ToBase64String(userId);

            // Generate user display name
            var userName = GenerateUserName(profileId);

            // Get existing credential IDs to exclude
            var existingPasskeys = await GetValidPasskeysAsync();
            var excludeCredentialIds = existingPasskeys
                .Select(a => a.CredentialBase64)
                .ToList();

            // Normalize authenticatorAttachment - only "platform" and "cross-platform" are valid WebAuthn values
            // "undefined", "all-supported", empty string, or null all mean "no preference"
            var normalizedAttachment = authenticatorAttachment switch {
                "platform" => "platform",
                "cross-platform" => "cross-platform",
                _ => null  // Any other value (including "undefined", "all-supported", "") means no preference
            };

            // Step 1: Create credential
            var createOptions = new CreateCredentialOptions {
                ExcludeCredentialIds = excludeCredentialIds,
                ResidentKey = residentKey,
                AuthenticatorAttachment = normalizedAttachment,
                UserVerification = userVerification,
                Attestation = attestationConveyancePreference,
                Hints = hints,
                UserIdBase64 = userIdBase64,
                UserName = userName,
                PrfSaltBase64 = prfSaltBase64
            };

            var createResult = await _credentialsBinding.CreateCredentialAsync(createOptions);
            if (createResult.IsFailed) {
                _logger.LogWarning("Failed to create credential: {Errors}", string.Join(", ", createResult.Errors));
                return Result.Fail<string>(createResult.Errors);
            }

            var credential = createResult.Value;
            _logger.LogInformation("Step 1 complete: Credential created with ID {CredentialId}", credential.CredentialId);

            // Notify user of step 1 success
            await _jsRuntime.InvokeVoidAsync("alert",
                "Step 1 of 2 creating passkey successful. Now, we'll confirm this authenticator and OS are sufficiently capable.");

            // Step 2: Get assertion to derive encryption key
            var getOptions = new GetCredentialOptions {
                AllowCredentialIds = [credential.CredentialId],
                TransportsPerCredential = [credential.Transports],
                UserVerification = userVerification,
                PrfSaltBase64 = prfSaltBase64
            };

            var assertionResult = await _credentialsBinding.GetCredentialAsync(getOptions);
            if (assertionResult.IsFailed) {
                _logger.LogWarning("Failed to get assertion: {Errors}", string.Join(", ", assertionResult.Errors));
                return Result.Fail<string>(assertionResult.Errors);
            }

            if (assertionResult.Value.PrfOutputBase64 is null) {
                return Result.Fail<string>("Authenticator did not return PRF output during verification");
            }

            // Derive encryption key from PRF output
            var prfOutput = Convert.FromBase64String(assertionResult.Value.PrfOutputBase64);
            var encryptionKey = _cryptoService.DeriveKeyFromPrf(profileId, prfOutput);

            // Get passcode from session storage
            var passcodeResult = await _storageService.GetItem<PasscodeModel>(StorageArea.Session);
            if (passcodeResult.IsFailed || passcodeResult.Value?.Passcode is null) {
                return Result.Fail<string>("No passcode is cached in session storage");
            }

            var passcode = passcodeResult.Value.Passcode;

            // Encrypt passcode
            var passcodeBytes = Encoding.UTF8.GetBytes(passcode);
            var encryptedPasscode = await _cryptoService.AesGcmEncryptAsync(encryptionKey, passcodeBytes, FixedNonce);
            var encryptedPasscodeBase64 = Convert.ToBase64String(encryptedPasscode);

            // Verify encrypt/decrypt roundtrip
            var decryptedPasscode = await _cryptoService.AesGcmDecryptAsync(encryptionKey, encryptedPasscode, FixedNonce);
            var decryptedPasscodeString = Encoding.UTF8.GetString(decryptedPasscode);
            if (decryptedPasscodeString != passcode) {
                _logger.LogError("Passcode encrypt/decrypt verification failed");
                return Result.Fail<string>("Passcode encryption verification failed");
            }

            // Get PasscodeHash from KeriaConnectConfig
            var configResult = await _storageService.GetItem<KeriaConnectConfig>(StorageArea.Local);
            if (configResult.IsFailed || configResult.Value is null) {
                return Result.Fail<string>("Could not retrieve KERIA configuration");
            }
            var passcodeHash = configResult.Value.PasscodeHash;

            // Compute transport intersection between requested and returned transports
            var requestedTransports = GetRequestedTransports(normalizedAttachment);
            var effectiveTransports = ComputeTransportIntersection(credential.Transports, requestedTransports);
            _logger.LogInformation(
                "Transport intersection: requested=[{Requested}], returned=[{Returned}], effective=[{Effective}]",
                string.Join(", ", requestedTransports),
                string.Join(", ", credential.Transports),
                string.Join(", ", effectiveTransports));

            // Get AAGUID, friendly name, and icon from metadata
            var aaguid = credential.Aaguid;
            var metadata = _fidoMetadataService.GetMetadata(aaguid);
            var descriptiveName = _fidoMetadataService.GenerateDescriptiveName(aaguid, effectiveTransports);
            var icon = metadata?.Icon;

            _logger.LogInformation(
                "Authenticator metadata: AAGUID={Aaguid}, Name={Name}, HasIcon={HasIcon}",
                aaguid, descriptiveName, icon is not null);

            // Create new passkey record
            var creationTime = DateTime.UtcNow;
            var newPasskey = new StoredPasskey {
                SchemaVersion = StoredPasskeySchema.CurrentVersion,
                Name = descriptiveName,
                CredentialBase64 = credential.CredentialId,
                Transports = effectiveTransports,
                EncryptedPasscodeBase64 = encryptedPasscodeBase64,
                PasscodeHash = passcodeHash,
                Aaguid = aaguid,
                Icon = icon,
                CreationTime = creationTime,
                LastUpdatedUtc = creationTime
            };

            // Add to storage (preserving ProfileId)
            var existingData = await GetStoredPasskeysDataAsync();
            var allPasskeys = existingData.Passkeys.ToList();
            allPasskeys.Add(newPasskey);

            var storeResult = await _storageService.SetItem(
                new StoredPasskeys { ProfileId = existingData.ProfileId, Passkeys = allPasskeys },
                StorageArea.Local);
            if (storeResult.IsFailed) {
                _logger.LogError("Failed to store passkey: {Errors}", string.Join(", ", storeResult.Errors));
                return Result.Fail<string>("Failed to store passkey");
            }

            _logger.LogInformation("Passkey created successfully: {Name}", newPasskey.Name);
            return Result.Ok(newPasskey.Name ?? "Unnamed Passkey");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Unexpected error during passkey creation");
            return Result.Fail<string>(new Error("Unexpected error during passkey creation").CausedBy(ex));
        }
    }

    public async Task<Result<string>> AuthenticateAndDecryptPasscodeAsync() {
        try {
            // Get valid passkeys
            var passkeys = await GetValidPasskeysAsync();
            if (passkeys.Count == 0) {
                _logger.LogWarning("No valid stored passkeys found");
                return Result.Fail<string>("No stored passkeys");
            }

            // Get profile identifier and compute PRF salt
            var profileId = await GetOrCreateProfileIdentifierAsync();
            var prfSalt = ComputePrfSalt(profileId);
            var prfSaltBase64 = Convert.ToBase64String(prfSalt);

            // Build credential options with per-credential transports
            var credentialIds = passkeys.Select(a => a.CredentialBase64).ToList();
            var transportsPerCredential = passkeys.Select(a => a.Transports).ToList();

            // Log transports for each credential to help diagnose passkey selection
            for (int i = 0; i < passkeys.Count; i++) {
                _logger.LogWarning(
                    "Authentication: Credential {Index} - Name: {Name}, CredentialId: {CredentialId}, Transports: [{Transports}]",
                    i,
                    passkeys[i].Name ?? "(unnamed)",
                    passkeys[i].CredentialBase64,
                    string.Join(", ", passkeys[i].Transports));
            }

            var getOptions = new GetCredentialOptions {
                AllowCredentialIds = credentialIds,
                TransportsPerCredential = transportsPerCredential,
                UserVerification = "preferred",
                PrfSaltBase64 = prfSaltBase64
            };

            var assertionResult = await _credentialsBinding.GetCredentialAsync(getOptions);
            if (assertionResult.IsFailed) {
                _logger.LogWarning("Failed to get assertion: {Errors}", string.Join(", ", assertionResult.Errors));
                return Result.Fail<string>(assertionResult.Errors);
            }

            var assertion = assertionResult.Value;
            if (assertion.PrfOutputBase64 is null) {
                return Result.Fail<string>("Authenticator did not return PRF output");
            }

            // Find matching passkey
            var matchingPasskey = passkeys.FirstOrDefault(a => a.CredentialBase64 == assertion.CredentialId);
            if (matchingPasskey is null) {
                _logger.LogError("Assertion credential ID does not match any stored passkey");
                return Result.Fail<string>("Credential not found in stored passkeys");
            }

            // Derive decryption key
            var prfOutput = Convert.FromBase64String(assertion.PrfOutputBase64);
            var decryptionKey = _cryptoService.DeriveKeyFromPrf(profileId, prfOutput);

            // Decrypt passcode
            var encryptedPasscode = Convert.FromBase64String(matchingPasskey.EncryptedPasscodeBase64);
            var decryptedPasscode = await _cryptoService.AesGcmDecryptAsync(decryptionKey, encryptedPasscode, FixedNonce);
            var passcode = Encoding.UTF8.GetString(decryptedPasscode);

            _logger.LogInformation("Successfully authenticated and decrypted passcode using credential {CredentialId}",
                assertion.CredentialId);
            return Result.Ok(passcode);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Unexpected error during authentication");
            return Result.Fail<string>(new Error("Could not decrypt passcode").CausedBy(ex));
        }
    }

    public async Task<Result<StoredPasskeys>> GetStoredPasskeysAsync() {
        var data = await GetStoredPasskeysDataAsync();
        var validPasskeys = data.Passkeys
            .Where(a => a.SchemaVersion == StoredPasskeySchema.CurrentVersion)
            .ToList();
        return Result.Ok(data with { Passkeys = validPasskeys });
    }

    public async Task<Result> RemovePasskeyAsync(string credentialBase64) {
        try {
            var existingData = await GetStoredPasskeysDataAsync();
            var allPasskeys = existingData.Passkeys.ToList();
            var removed = allPasskeys.RemoveAll(a => a.CredentialBase64 == credentialBase64);

            if (removed == 0) {
                return Result.Fail("Passkey not found");
            }

            var storeResult = await _storageService.SetItem(
                new StoredPasskeys { ProfileId = existingData.ProfileId, Passkeys = allPasskeys },
                StorageArea.Local);
            if (storeResult.IsFailed) {
                return Result.Fail(storeResult.Errors);
            }

            _logger.LogInformation("Removed passkey with credential ID {CredentialId}", credentialBase64);
            return Result.Ok();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error removing passkey");
            return Result.Fail(new Error("Failed to remove passkey").CausedBy(ex));
        }
    }

    public async Task<Result> TestPasskeyAsync(string credentialBase64) {
        try {
            // Find the specific passkey
            var passkeys = await GetValidPasskeysAsync();
            var passkey = passkeys.FirstOrDefault(a => a.CredentialBase64 == credentialBase64);

            if (passkey is null) {
                _logger.LogWarning("Passkey not found for testing: {CredentialId}", credentialBase64);
                return Result.Fail("Passkey not found");
            }

            // Get profile identifier and compute PRF salt
            var profileId = await GetOrCreateProfileIdentifierAsync();
            var prfSalt = ComputePrfSalt(profileId);
            var prfSaltBase64 = Convert.ToBase64String(prfSalt);

            _logger.LogWarning(
                "Testing specific passkey - Name: {Name}, CredentialId: {CredentialId}, Transports: [{Transports}]",
                passkey.Name ?? "(unnamed)",
                passkey.CredentialBase64,
                string.Join(", ", passkey.Transports));

            // Build options for only this specific credential
            var getOptions = new GetCredentialOptions {
                AllowCredentialIds = [passkey.CredentialBase64],
                TransportsPerCredential = [passkey.Transports],
                UserVerification = "preferred",
                PrfSaltBase64 = prfSaltBase64
            };

            var assertionResult = await _credentialsBinding.GetCredentialAsync(getOptions);
            if (assertionResult.IsFailed) {
                _logger.LogWarning("Test failed for passkey {Name}: {Errors}",
                    passkey.Name, string.Join(", ", assertionResult.Errors));
                return Result.Fail(assertionResult.Errors);
            }

            var assertion = assertionResult.Value;
            if (assertion.PrfOutputBase64 is null) {
                return Result.Fail("Authenticator did not return PRF output during test");
            }

            // Verify we can decrypt the passcode
            var prfOutput = Convert.FromBase64String(assertion.PrfOutputBase64);
            var decryptionKey = _cryptoService.DeriveKeyFromPrf(profileId, prfOutput);
            var encryptedPasscode = Convert.FromBase64String(passkey.EncryptedPasscodeBase64);

            try {
                var decryptedPasscode = await _cryptoService.AesGcmDecryptAsync(decryptionKey, encryptedPasscode, FixedNonce);
                _logger.LogInformation("Test successful for passkey {Name}", passkey.Name);
                return Result.Ok();
            }
            catch (Exception decryptEx) {
                _logger.LogError(decryptEx, "Test failed - could not decrypt passcode for passkey {Name}", passkey.Name);
                return Result.Fail("Decryption failed - passkey may have been created with different profile");
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Unexpected error during passkey test");
            return Result.Fail(new Error("Test failed unexpectedly").CausedBy(ex));
        }
    }

    public async Task<Result<string>> TestAllPasskeysAsync() {
        try {
            // Get all valid passkeys
            var passkeys = await GetValidPasskeysAsync();
            if (passkeys.Count == 0) {
                _logger.LogWarning("No valid stored passkeys found for testing");
                return Result.Fail<string>("No stored passkeys");
            }

            // Get profile identifier and compute PRF salt
            var profileId = await GetOrCreateProfileIdentifierAsync();
            var prfSalt = ComputePrfSalt(profileId);
            var prfSaltBase64 = Convert.ToBase64String(prfSalt);

            // Build credential options with per-credential transports (same as AuthenticateAndDecryptPasscodeAsync)
            var credentialIds = passkeys.Select(a => a.CredentialBase64).ToList();
            var transportsPerCredential = passkeys.Select(a => a.Transports).ToList();

            _logger.LogWarning("Testing all {Count} passkeys with stored transports", passkeys.Count);
            foreach (var pk in passkeys) {
                _logger.LogWarning(
                    "  - Name: {Name}, CredentialId: {CredentialId}, Stored Transports: [{Transports}]",
                    pk.Name ?? "(unnamed)",
                    pk.CredentialBase64,
                    string.Join(", ", pk.Transports));
            }

            var getOptions = new GetCredentialOptions {
                AllowCredentialIds = credentialIds,
                TransportsPerCredential = transportsPerCredential,
                UserVerification = "preferred",
                PrfSaltBase64 = prfSaltBase64
            };

            var assertionResult = await _credentialsBinding.GetCredentialAsync(getOptions);
            if (assertionResult.IsFailed) {
                _logger.LogWarning("Test All failed: {Errors}", string.Join(", ", assertionResult.Errors));
                return Result.Fail<string>(assertionResult.Errors);
            }

            var assertion = assertionResult.Value;
            if (assertion.PrfOutputBase64 is null) {
                return Result.Fail<string>("Authenticator did not return PRF output during test");
            }

            // Find which passkey was used
            var usedPasskey = passkeys.FirstOrDefault(a => a.CredentialBase64 == assertion.CredentialId);
            if (usedPasskey is null) {
                _logger.LogError("Assertion credential ID does not match any stored passkey");
                return Result.Fail<string>("Unknown credential was used");
            }

            // Verify we can decrypt the passcode
            var prfOutput = Convert.FromBase64String(assertion.PrfOutputBase64);
            var decryptionKey = _cryptoService.DeriveKeyFromPrf(profileId, prfOutput);
            var encryptedPasscode = Convert.FromBase64String(usedPasskey.EncryptedPasscodeBase64);

            try {
                var decryptedPasscode = await _cryptoService.AesGcmDecryptAsync(decryptionKey, encryptedPasscode, FixedNonce);
                _logger.LogInformation("Test All successful using passkey {Name}", usedPasskey.Name);
                return Result.Ok(usedPasskey.Name ?? "Unnamed Passkey");
            }
            catch (Exception decryptEx) {
                _logger.LogError(decryptEx, "Test All failed - could not decrypt passcode using passkey {Name}",
                    usedPasskey.Name);
                return Result.Fail<string>("Decryption failed - passkey may have been created with different profile");
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Unexpected error during Test All");
            return Result.Fail<string>(new Error("Test All failed unexpectedly").CausedBy(ex));
        }
    }

    /// <summary>
    /// Gets the full StoredPasskeys data including ProfileId.
    /// Creates a new ProfileId if none exists.
    /// </summary>
    private async Task<StoredPasskeys> GetStoredPasskeysDataAsync() {
        var result = await _storageService.GetItem<StoredPasskeys>(StorageArea.Local);
        if (result.IsSuccess && result.Value is not null) {
            // Ensure ProfileId exists (migration from old data)
            if (result.Value.ProfileId is not null) {
                return result.Value;
            }
            // Migrate: add ProfileId to existing data
            var newProfileId = Guid.NewGuid().ToString();
            var updatedData = result.Value with { ProfileId = newProfileId };
            await _storageService.SetItem(updatedData, StorageArea.Local);
            _logger.LogInformation("Migrated StoredPasskeys with new profile identifier");
            return updatedData;
        }

        // Create new structure with ProfileId
        var newId = Guid.NewGuid().ToString();
        var newData = new StoredPasskeys { ProfileId = newId, Passkeys = [] };
        await _storageService.SetItem(newData, StorageArea.Local);
        _logger.LogInformation("Created new StoredPasskeys with profile identifier");
        return newData;
    }

    /// <summary>
    /// Gets or creates the browser profile identifier stored in local storage.
    /// </summary>
    private async Task<string> GetOrCreateProfileIdentifierAsync() {
        var data = await GetStoredPasskeysDataAsync();
        return data.ProfileId!;
    }

    /// <summary>
    /// Computes the PRF salt from the profile identifier using SHA-256.
    /// </summary>
    private byte[] ComputePrfSalt(string profileId) {
        return _cryptoService.Sha256(Encoding.UTF8.GetBytes(profileId));
    }

    /// <summary>
    /// Generates a user-friendly name for the WebAuthn credential based on profile identifier.
    /// </summary>
    private string GenerateUserName(string profileId) {
        // Generate a 6-digit hash for easy identification
        var hash = _cryptoService.Sha256(Encoding.UTF8.GetBytes(profileId + "fixed"));
        var hashHex = Convert.ToHexString(hash).ToLowerInvariant();
        var numericValue = Convert.ToInt64(hashHex[..10], 16) % 1000000;
        var sixDigit = numericValue.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        return $"{KeriAuthExtensionName} ({sixDigit})";
    }

    /// <summary>
    /// Gets all passkeys from storage, regardless of schema version.
    /// </summary>
    private async Task<List<StoredPasskey>> GetAllPasskeysFromStorageAsync() {
        var data = await GetStoredPasskeysDataAsync();
        return data.Passkeys.ToList();
    }

    /// <summary>
    /// Gets only passkeys with valid (current) schema version.
    /// Old passkeys are silently filtered out.
    /// </summary>
    private async Task<List<StoredPasskey>> GetValidPasskeysAsync() {
        var all = await GetAllPasskeysFromStorageAsync();
        var valid = all.Where(a => a.SchemaVersion == StoredPasskeySchema.CurrentVersion).ToList();

        if (valid.Count < all.Count) {
            _logger.LogInformation("Filtered out {Count} passkeys with old schema version",
                all.Count - valid.Count);
        }

        return valid;
    }

    /// <summary>
    /// Gets the set of transports that were requested based on authenticator attachment preference.
    /// </summary>
    private static string[] GetRequestedTransports(string? authenticatorAttachment) {
        return authenticatorAttachment switch {
            "platform" => ["internal"],
            "cross-platform" => ["usb", "nfc", "ble", "hybrid"],
            _ => ["usb", "nfc", "ble", "internal", "hybrid"]  // No preference - all transports
        };
    }

    /// <summary>
    /// Computes the intersection of returned transports and requested transports.
    /// This provides the most accurate transport hints for subsequent authentication.
    /// </summary>
    private static string[] ComputeTransportIntersection(string[] returnedTransports, string[] requestedTransports) {
        if (returnedTransports.Length == 0) {
            // If getTransports() returned empty, use the requested transports as fallback
            return requestedTransports;
        }

        var requestedSet = new HashSet<string>(requestedTransports, StringComparer.OrdinalIgnoreCase);
        var intersection = returnedTransports
            .Where(t => requestedSet.Contains(t))
            .ToArray();

        // If intersection is empty (shouldn't happen in practice), fall back to returned transports
        return intersection.Length > 0 ? intersection : returnedTransports;
    }
}
