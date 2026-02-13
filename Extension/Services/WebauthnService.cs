using FluentResults;
using Extension.Models;
using Extension.Models.Storage;
using Extension.Services.Crypto;
using Extension.Services.JsBindings;
using Extension.Services.Storage;
using Extension.Utilities;
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

    private static readonly string KeriAuthExtensionName = AppConfig.ProductName;

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
            // Get KERIA connection digest and compute PRF salt
            var keriaConnectionDigestResult = await GetCurrentKeriaConnectionDigestAsync();
            if (keriaConnectionDigestResult.IsFailed) {
                return Result.Fail<string>(keriaConnectionDigestResult.Errors);
            }
            var keriaConnectionDigest = keriaConnectionDigestResult.Value;
            var prfSalt = ComputePrfSalt(keriaConnectionDigest);
            var prfSaltBase64 = Convert.ToBase64String(prfSalt);

            // Compute user ID from KERIA connection digest
            var userId = _cryptoService.Sha256(Encoding.UTF8.GetBytes(keriaConnectionDigest));
            var userIdBase64 = Convert.ToBase64String(userId);

            // Generate user display name
            var userName = GenerateUserName(keriaConnectionDigest);

            // Get existing credential IDs to exclude - only exclude passkeys for the CURRENT config
            // Passkeys for other configs have different userIds, so they won't conflict
            var existingPasskeys = await GetValidPasskeysAsync();
            var passkeysForCurrentConfig = existingPasskeys
                .Where(p => p.KeriaConnectionDigest == keriaConnectionDigest)
                .ToList();
            var excludeCredentialIds = passkeysForCurrentConfig
                .Select(a => a.CredentialBase64)
                .ToList();

            _logger.LogDebug(
                nameof(RegisterAttestStoreAuthenticatorAsync) + ": excludeCount={ExcludeCount} (total passkeys={Total}, for current config={ForConfig})",
                excludeCredentialIds.Count,
                existingPasskeys.Count,
                passkeysForCurrentConfig.Count);

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
                _logger.LogWarning(nameof(RegisterAttestStoreAuthenticatorAsync) + ": Failed to create credential: {Errors}", string.Join(", ", createResult.Errors));
                return Result.Fail<string>(createResult.Errors);
            }

            var credential = createResult.Value;
            _logger.LogInformation(nameof(RegisterAttestStoreAuthenticatorAsync) + ": Step 1 complete: Credential created with ID {CredentialId}", credential.CredentialId);

            // Notify user of step 1 success
            await _jsRuntime.InvokeVoidAsync("alert",
                "Step 1 of 2 creating passkey successful. Now, we'll confirm this authenticator and platform are sufficiently capable.");

            // Step 2: Get assertion to derive encryption key
            var getOptions = new GetCredentialOptions {
                AllowCredentialIds = [credential.CredentialId],
                TransportsPerCredential = [credential.Transports],
                UserVerification = userVerification,
                PrfSaltBase64 = prfSaltBase64
            };

            var assertionResult = await _credentialsBinding.GetCredentialAsync(getOptions);
            if (assertionResult.IsFailed) {
                _logger.LogWarning(nameof(RegisterAttestStoreAuthenticatorAsync) + ": Failed to get assertion: {Errors}", string.Join(", ", assertionResult.Errors));
                return Result.Fail<string>(assertionResult.Errors);
            }

            if (assertionResult.Value.PrfOutputBase64 is null) {
                return Result.Fail<string>("Authenticator did not return PRF output during verification");
            }

            // Derive encryption key from PRF output
            var prfOutput = Convert.FromBase64String(assertionResult.Value.PrfOutputBase64);
            var encryptionKey = _cryptoService.DeriveKeyFromPrf(keriaConnectionDigest, prfOutput);

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
                _logger.LogError(nameof(RegisterAttestStoreAuthenticatorAsync) + ": Passcode encrypt/decrypt verification failed");
                return Result.Fail<string>("Passcode encryption verification failed");
            }

            // Compute transport intersection between requested and returned transports
            var requestedTransports = GetRequestedTransports(normalizedAttachment);
            var effectiveTransports = ComputeTransportIntersection(credential.Transports, requestedTransports);
            _logger.LogInformation(
                nameof(RegisterAttestStoreAuthenticatorAsync) + ": Transport intersection: requested=[{Requested}], returned=[{Returned}], effective=[{Effective}]",
                string.Join(", ", requestedTransports),
                string.Join(", ", credential.Transports),
                string.Join(", ", effectiveTransports));

            // Get AAGUID, friendly name, and icon from metadata
            var aaguid = credential.Aaguid;
            var metadata = _fidoMetadataService.GetMetadata(aaguid);
            var descriptiveName = _fidoMetadataService.GenerateDescriptiveName(aaguid, effectiveTransports);
            var icon = metadata?.Icon;

            _logger.LogInformation(
                nameof(RegisterAttestStoreAuthenticatorAsync) + ": Authenticator metadata: AAGUID={Aaguid}, Name={Name}, HasIcon={HasIcon}",
                aaguid, descriptiveName, icon is not null);

            // Create new passkey record
            var creationTime = DateTime.UtcNow;
            var newPasskey = new StoredPasskey {
                SchemaVersion = StoredPasskeySchema.CurrentVersion,
                Name = descriptiveName,
                CredentialBase64 = credential.CredentialId,
                Transports = effectiveTransports,
                EncryptedPasscodeBase64 = encryptedPasscodeBase64,
                KeriaConnectionDigest = keriaConnectionDigest,
                Aaguid = aaguid,
                Icon = icon,
                CreationTime = creationTime,
                LastUpdatedUtc = creationTime
            };

            // Add to storage
            var existingData = await GetStoredPasskeysDataAsync();
            var allPasskeys = existingData.Passkeys.ToList();
            allPasskeys.Add(newPasskey);

            var storeResult = await _storageService.SetItem(
                new StoredPasskeys { Passkeys = allPasskeys, IsStored = true },
                StorageArea.Local);
            if (storeResult.IsFailed) {
                _logger.LogError(nameof(RegisterAttestStoreAuthenticatorAsync) + ": Failed to store passkey: {Errors}", string.Join(", ", storeResult.Errors));
                return Result.Fail<string>("Failed to store passkey");
            }

            _logger.LogInformation(nameof(RegisterAttestStoreAuthenticatorAsync) + ": Passkey created successfully: {Name}", newPasskey.Name);
            return Result.Ok(newPasskey.Name ?? "Unnamed Passkey");
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(RegisterAttestStoreAuthenticatorAsync) + ": Unexpected error during passkey creation");
            return Result.Fail<string>(new Error("Unexpected error during passkey creation").CausedBy(ex));
        }
    }

    public async Task<Result<string>> AuthenticateAndDecryptPasscodeAsync() {
        try {
            // Get KERIA connection digest first - we need it to filter passkeys
            var keriaConnectionDigestResult = await GetCurrentKeriaConnectionDigestAsync();
            if (keriaConnectionDigestResult.IsFailed) {
                return Result.Fail<string>(keriaConnectionDigestResult.Errors);
            }
            var keriaConnectionDigest = keriaConnectionDigestResult.Value;

            // Get valid passkeys for the CURRENT config only
            // Passkeys are tied to their config via PRF salt - using a passkey from a different config
            // would result in a different PRF output and decryption failure
            var allPasskeys = await GetValidPasskeysAsync();
            var passkeys = allPasskeys
                .Where(p => p.KeriaConnectionDigest == keriaConnectionDigest)
                .ToList();

            _logger.LogDebug(
                nameof(AuthenticateAndDecryptPasscodeAsync) + ": passkeysForConfig={ForConfig} (total={Total})",
                passkeys.Count,
                allPasskeys.Count);

            if (passkeys.Count == 0) {
                _logger.LogInformation(nameof(AuthenticateAndDecryptPasscodeAsync) + ": No valid stored passkeys found for current config");
                return Result.Fail<string>("No stored passkeys for this configuration");
            }

            // Compute PRF salt from current config's digest
            var prfSalt = ComputePrfSalt(keriaConnectionDigest);
            var prfSaltBase64 = Convert.ToBase64String(prfSalt);

            // Build credential options with per-credential transports
            var credentialIds = passkeys.Select(a => a.CredentialBase64).ToList();
            var transportsPerCredential = passkeys.Select(a => a.Transports).ToList();

            // Log transports for each credential to help diagnose passkey selection
            for (int i = 0; i < passkeys.Count; i++) {
                _logger.LogInformation(
                    nameof(AuthenticateAndDecryptPasscodeAsync) + ": Credential {Index} - Name: {Name}, CredentialId: {CredentialId}, KeriaDigest: {KeriaDigest}, Transports: [{Transports}]",
                    i,
                    passkeys[i].Name ?? "(unnamed)",
                    passkeys[i].CredentialBase64,
                    passkeys[i].KeriaConnectionDigest,
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
                _logger.LogWarning(nameof(AuthenticateAndDecryptPasscodeAsync) + ": Failed to get assertion: {Errors}", string.Join(", ", assertionResult.Errors));
                return Result.Fail<string>(assertionResult.Errors);
            }

            var assertion = assertionResult.Value;
            if (assertion.PrfOutputBase64 is null) {
                return Result.Fail<string>("Authenticator did not return PRF output");
            }

            // Find matching passkey
            var matchingPasskey = passkeys.FirstOrDefault(a => a.CredentialBase64 == assertion.CredentialId);
            if (matchingPasskey is null) {
                _logger.LogError(nameof(AuthenticateAndDecryptPasscodeAsync) + ": Assertion credential ID does not match any stored passkey");
                return Result.Fail<string>("Credential not found in stored passkeys");
            }

            // Derive decryption key
            var prfOutput = Convert.FromBase64String(assertion.PrfOutputBase64);
            var decryptionKey = _cryptoService.DeriveKeyFromPrf(keriaConnectionDigest, prfOutput);

            // Decrypt passcode
            var encryptedPasscode = Convert.FromBase64String(matchingPasskey.EncryptedPasscodeBase64);
            var decryptedPasscode = await _cryptoService.AesGcmDecryptAsync(decryptionKey, encryptedPasscode, FixedNonce);
            var passcode = Encoding.UTF8.GetString(decryptedPasscode);

            _logger.LogInformation(nameof(AuthenticateAndDecryptPasscodeAsync) + ": Successfully authenticated and decrypted passcode using credential {CredentialId}",
                assertion.CredentialId);
            return Result.Ok(passcode);
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(AuthenticateAndDecryptPasscodeAsync) + ": Unexpected error during authentication");
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
                new StoredPasskeys { Passkeys = allPasskeys, IsStored = true },
                StorageArea.Local);
            if (storeResult.IsFailed) {
                return Result.Fail(storeResult.Errors);
            }

            _logger.LogInformation(nameof(RemovePasskeyAsync) + ": Removed passkey with credential ID {CredentialId}", credentialBase64);
            return Result.Ok();
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(RemovePasskeyAsync) + ": Error removing passkey");
            return Result.Fail(new Error("Failed to remove passkey").CausedBy(ex));
        }
    }

    public async Task<Result> TestPasskeyAsync(string credentialBase64) {
        try {
            // Find the specific passkey
            var passkeys = await GetValidPasskeysAsync();
            var passkey = passkeys.FirstOrDefault(a => a.CredentialBase64 == credentialBase64);

            if (passkey is null) {
                _logger.LogWarning(nameof(TestPasskeyAsync) + ": Passkey not found for testing: {CredentialId}", credentialBase64);
                return Result.Fail("Passkey not found");
            }

            // Get KERIA connection digest and compute PRF salt
            var keriaConnectionDigestResult = await GetCurrentKeriaConnectionDigestAsync();
            if (keriaConnectionDigestResult.IsFailed) {
                return Result.Fail(keriaConnectionDigestResult.Errors);
            }
            var keriaConnectionDigest = keriaConnectionDigestResult.Value;
            var prfSalt = ComputePrfSalt(keriaConnectionDigest);
            var prfSaltBase64 = Convert.ToBase64String(prfSalt);

            _logger.LogInformation(
                nameof(TestPasskeyAsync) + ": Testing specific passkey - Name: {Name}, CredentialId: {CredentialId}, Transports: [{Transports}]",
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
                _logger.LogWarning(nameof(TestPasskeyAsync) + ": Test failed for passkey {Name}: {Errors}",
                    passkey.Name, string.Join(", ", assertionResult.Errors));
                return Result.Fail(assertionResult.Errors);
            }

            var assertion = assertionResult.Value;
            if (assertion.PrfOutputBase64 is null) {
                return Result.Fail("Authenticator did not return PRF output during test");
            }

            // Verify we can decrypt the passcode
            var prfOutput = Convert.FromBase64String(assertion.PrfOutputBase64);
            var decryptionKey = _cryptoService.DeriveKeyFromPrf(keriaConnectionDigest, prfOutput);
            var encryptedPasscode = Convert.FromBase64String(passkey.EncryptedPasscodeBase64);

            try {
                var decryptedPasscode = await _cryptoService.AesGcmDecryptAsync(decryptionKey, encryptedPasscode, FixedNonce);
                _logger.LogInformation(nameof(TestPasskeyAsync) + ": Test successful for passkey {Name}", passkey.Name);
                return Result.Ok();
            }
            catch (Exception decryptEx) {
                _logger.LogError(decryptEx, nameof(TestPasskeyAsync) + ": Test failed - could not decrypt passcode for passkey {Name}", passkey.Name);
                return Result.Fail("Decryption failed - passkey may have been created with different KERIA connection");
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(TestPasskeyAsync) + ": Unexpected error during passkey test");
            return Result.Fail(new Error("Test failed unexpectedly").CausedBy(ex));
        }
    }

    public async Task<Result<string>> TestAllPasskeysAsync() {
        try {
            // Get all valid passkeys
            var passkeys = await GetValidPasskeysAsync();
            if (passkeys.Count == 0) {
                _logger.LogWarning(nameof(TestAllPasskeysAsync) + ": No valid stored passkeys found for testing");
                return Result.Fail<string>("No stored passkeys");
            }

            // Get KERIA connection digest and compute PRF salt
            var keriaConnectionDigestResult = await GetCurrentKeriaConnectionDigestAsync();
            if (keriaConnectionDigestResult.IsFailed) {
                return Result.Fail<string>(keriaConnectionDigestResult.Errors);
            }
            var keriaConnectionDigest = keriaConnectionDigestResult.Value;
            var prfSalt = ComputePrfSalt(keriaConnectionDigest);
            var prfSaltBase64 = Convert.ToBase64String(prfSalt);

            // Build credential options with per-credential transports (same as AuthenticateAndDecryptPasscodeAsync)
            var credentialIds = passkeys.Select(a => a.CredentialBase64).ToList();
            var transportsPerCredential = passkeys.Select(a => a.Transports).ToList();

            _logger.LogInformation(nameof(TestAllPasskeysAsync) + ": Testing all {Count} passkeys with stored transports", passkeys.Count);
            foreach (var pk in passkeys) {
                _logger.LogInformation(
                    nameof(TestAllPasskeysAsync) + ": Name: {Name}, CredentialId: {CredentialId}, Stored Transports: [{Transports}]",
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
                _logger.LogWarning(nameof(TestAllPasskeysAsync) + ": Test All failed: {Errors}", string.Join(", ", assertionResult.Errors));
                return Result.Fail<string>(assertionResult.Errors);
            }

            var assertion = assertionResult.Value;
            if (assertion.PrfOutputBase64 is null) {
                return Result.Fail<string>("Authenticator did not return PRF output during test");
            }

            // Find which passkey was used
            var usedPasskey = passkeys.FirstOrDefault(a => a.CredentialBase64 == assertion.CredentialId);
            if (usedPasskey is null) {
                _logger.LogError(nameof(TestAllPasskeysAsync) + ": Assertion credential ID does not match any stored passkey");
                return Result.Fail<string>("Unknown credential was used");
            }

            // Verify we can decrypt the passcode
            var prfOutput = Convert.FromBase64String(assertion.PrfOutputBase64);
            var decryptionKey = _cryptoService.DeriveKeyFromPrf(keriaConnectionDigest, prfOutput);
            var encryptedPasscode = Convert.FromBase64String(usedPasskey.EncryptedPasscodeBase64);

            try {
                var decryptedPasscode = await _cryptoService.AesGcmDecryptAsync(decryptionKey, encryptedPasscode, FixedNonce);
                _logger.LogInformation(nameof(TestAllPasskeysAsync) + ": Test All successful using passkey {Name}", usedPasskey.Name);
                return Result.Ok(usedPasskey.Name ?? "Unnamed Passkey");
            }
            catch (Exception decryptEx) {
                _logger.LogError(decryptEx, nameof(TestAllPasskeysAsync) + ": Test All failed - could not decrypt passcode using passkey {Name}",
                    usedPasskey.Name);
                return Result.Fail<string>("Decryption failed - passkey may have been created with different KERIA connection");
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(TestAllPasskeysAsync) + ": Unexpected error during Test All");
            return Result.Fail<string>(new Error("Test All failed unexpectedly").CausedBy(ex));
        }
    }

    /// <summary>
    /// Gets the full StoredPasskeys data from local storage.
    /// KeriaConnectionDigest is now computed from KeriaConnectConfig, not stored at collection level.
    /// </summary>
    private async Task<StoredPasskeys> GetStoredPasskeysDataAsync() {
        var result = await _storageService.GetItem<StoredPasskeys>(StorageArea.Local);
        if (result.IsSuccess && result.Value is not null) {
            return result.Value;
        }

        // Return empty structure - KeriaConnectionDigest is computed separately from KeriaConnectConfig
        return new StoredPasskeys { Passkeys = [] };
    }

    /// <summary>
    /// Gets the KERIA connection digest from the selected configuration in Preferences.
    /// Returns the SelectedKeriaConnectionDigest directly since it's already computed and stored.
    /// </summary>
    public async Task<Result<string>> GetCurrentKeriaConnectionDigestAsync() {
        var prefsResult = await _storageService.GetItem<Preferences>(StorageArea.Local);
        if (prefsResult.IsFailed || prefsResult.Value is null) {
            return Result.Fail<string>("Could not retrieve Preferences for KeriaConnectionDigest");
        }
        var digest = prefsResult.Value.KeriaPreference.SelectedKeriaConnectionDigest;
        if (string.IsNullOrEmpty(digest)) {
            return Result.Fail<string>("No KERIA configuration selected");
        }
        return Result.Ok(digest);
    }

    /// <summary>
    /// Computes the PRF salt from the KERIA connection digest using SHA-256.
    /// </summary>
    private byte[] ComputePrfSalt(string keriaConnectionDigest) {
        return _cryptoService.Sha256(Encoding.UTF8.GetBytes(keriaConnectionDigest));
    }

    /// <summary>
    /// Computes the KeriaConnectionDigest using the shared helper.
    /// Delegates to KeriaConnectionDigestHelper.Compute() for consistent digest computation.
    /// </summary>
    private static Result<string> ComputeKeriaConnectionDigest(KeriaConnectConfig config) =>
        KeriaConnectionDigestHelper.Compute(config);

    /// <summary>
    /// Generates a user-friendly name for the WebAuthn credential based on KERIA connection digest.
    /// </summary>
    private string GenerateUserName(string keriaConnectionDigest) {
        // Generate a 6-digit hash for easy identification
        var hash = _cryptoService.Sha256(Encoding.UTF8.GetBytes(keriaConnectionDigest + "fixed"));
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
            _logger.LogInformation(nameof(GetValidPasskeysAsync) + ": Filtered out {Count} passkeys with old schema version",
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
