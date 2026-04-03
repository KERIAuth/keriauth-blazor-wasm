using System.Globalization;
using Extension.Models;
using Extension.Models.Messages.AppBw;
using Extension.Models.Storage;
using Extension.Services.SignifyService;
using Extension.Services.Storage;
using Extension.Utilities;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace Extension.Services.ConfigureService;

public class ConfigureService : IConfigureService {
    private readonly ISignifyClientService _signifyClient;
    private readonly IStorageService _storageService;
    private readonly SessionManager _sessionManager;
    private readonly ILogger<ConfigureService> _logger;

    private const int TotalSteps = 6;

    public ConfigureService(
        ISignifyClientService signifyClient,
        IStorageService storageService,
        SessionManager sessionManager,
        ILogger<ConfigureService> logger) {
        _signifyClient = signifyClient;
        _storageService = storageService;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task<Result<ConfigureResponsePayload>> ConfigureAsync(ConfigureRequestPayload payload) {
        _logger.LogInformation("ConfigureAsync starting for {AdminUrl}", payload.AdminUrl);

        string? partialDigest = null;

        try {
            // Step 1: Store partial KeriaConnectConfig
            await ReportProgress(1, TotalSteps, "Storing configuration");

            var passcodeHash = DeterministicHash.ComputeHash(payload.Passcode);
            var partialConfig = new KeriaConnectConfig(
                providerName: payload.ProviderName,
                adminUrl: payload.AdminUrl,
                bootUrl: payload.BootUrl,
                passcodeHash: passcodeHash,
                clientAidPrefix: null,
                agentAidPrefix: null,
                isStored: true
            ) { Alias = "Configured at " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "UTC" };

            var storePartialResult = await StoreConfigAsync(partialConfig, setAsSelected: false);
            if (storePartialResult.IsFailed) {
                return await FailAsync(1, "Failed to store connection configuration", storePartialResult);
            }
            partialDigest = storePartialResult.Value;

            // Step 2: Connect to KERIA
            await ReportProgress(2, TotalSteps, "Connecting to KERIA");

            Result<SignifyService.Models.State> connectResult;
            using (_signifyClient.BeginLongOperation()) {
                connectResult = await _signifyClient.Connect(
                    payload.AdminUrl,
                    payload.Passcode,
                    payload.BootUrl,
                    payload.IsNewAccount,
                    payload.BootAuthUsername,
                    payload.BootAuthPassword);
            }

            if (connectResult.IsFailed || connectResult.Value is null) {
                var errorDetail = connectResult.Errors.Count > 0 ? connectResult.Errors[0].Message : "Unknown error";
                var userMessage = BuildConnectErrorMessage(errorDetail, payload);
                return await FailAndCleanupAsync(2, userMessage);
            }

            var clientAidPrefix = connectResult.Value.Controller?.State?.I;
            var agentAidPrefix = connectResult.Value.Agent?.I;

            // Step 3: Store complete config + unlock session
            await ReportProgress(3, TotalSteps, "Storing configuration and unlocking session");

            var completeConfig = partialConfig with {
                ClientAidPrefix = clientAidPrefix,
                AgentAidPrefix = agentAidPrefix
            };

            var storeCompleteResult = await StoreConfigAsync(completeConfig, setAsSelected: true, previousDigest: partialDigest);
            if (storeCompleteResult.IsFailed) {
                return await FailAndCleanupAsync(3, "Failed to store complete configuration", storeCompleteResult.Errors);
            }
            var completeDigest = storeCompleteResult.Value;
            // Clear partial digest tracking — now tracking the complete digest
            partialDigest = null;

            var unlockResult = await _sessionManager.UnlockSessionAsync(payload.Passcode);
            if (unlockResult.IsFailed) {
                return await FailAndCleanupAsync(3, unlockResult.Errors.Count > 0 ? unlockResult.Errors[0].Message : "Unlock failed");
            }

            // Step 4: Retrieve identifiers
            await ReportProgress(4, TotalSteps, "Retrieving identifiers");

            var identifiersResult = await _signifyClient.GetIdentifiers();
            if (identifiersResult.IsFailed || identifiersResult.Value is null) {
                return await FailAndCleanupAsync(4, "Failed to retrieve identifiers");
            }

            var identifiers = identifiersResult.Value;

            // Step 5: Create identifier if none exist
            if (identifiers.Aids.Count == 0) {
                await ReportProgress(5, TotalSteps, "Creating personal profile");

                var createResult = await _signifyClient.CreateAidWithEndRole("Alias1");
                if (createResult.IsFailed) {
                    return await FailAndCleanupAsync(5, "Failed to create identifier", createResult.Errors);
                }

                // Re-fetch identifiers after creation
                var refreshResult = await _signifyClient.GetIdentifiers();
                if (refreshResult.IsFailed || refreshResult.Value is null || refreshResult.Value.Aids.Count == 0) {
                    return await FailAndCleanupAsync(5, "Failed to retrieve identifiers after creation");
                }
                identifiers = refreshResult.Value;
            } else {
                await ReportProgress(5, TotalSteps, "Existing identifiers found");
            }

            // Step 6: Set selected prefix + store KeriaConnectionInfo + mark proven
            await ReportProgress(6, TotalSteps, "Finalizing configuration");

            var selectedPrefix = identifiers.Aids[0].Prefix;

            // Update config with SelectedPrefix and mark as proven
            var existingConfigs = await GetConfigsAsync();
            if (existingConfigs.Configs.TryGetValue(completeDigest, out var currentConfig)) {
                var provenConfig = currentConfig with {
                    SelectedPrefix = selectedPrefix,
                    ProvenAt = DateTime.UtcNow
                };
                var updatedDict = new Dictionary<string, KeriaConnectConfig>(existingConfigs.Configs) {
                    [completeDigest] = provenConfig
                };
                var storeResult = await _storageService.SetItem(existingConfigs with { Configs = updatedDict });
                if (storeResult.IsFailed) {
                    return await FailAndCleanupAsync(6, "Failed to store selected identifier", storeResult.Errors);
                }
            } else {
                return await FailAndCleanupAsync(6, "Configuration not found for computed digest");
            }

            // Store KeriaConnectionInfo to session storage
            var connectionInfo = new KeriaConnectionInfo {
                KeriaConnectionDigest = completeDigest,
                IdentifiersList = [identifiers]
            };
            var storeConnectionResult = await _storageService.SetItem(connectionInfo, StorageArea.Session);
            if (storeConnectionResult.IsFailed) {
                return await FailAndCleanupAsync(6, "Failed to cache connection info", storeConnectionResult.Errors);
            }

            await ReportComplete();
            _logger.LogInformation("ConfigureAsync completed successfully");
            return Result.Ok(new ConfigureResponsePayload(true));
        }
        catch (Exception ex) {
            _logger.LogError(ex, "ConfigureAsync failed with exception");
            await CleanupUnprovenConfigsAsync();
            await ReportError(ex.Message);
            return Result.Ok(new ConfigureResponsePayload(false, Error: ex.Message));
        }
    }

    public async Task<Result> ResetAsync() {
        _logger.LogInformation("ResetAsync: Cleaning up session and unproven configs");

        // Remove session-scoped items
        await _storageService.RemoveItem<SessionStateModel>(StorageArea.Session);
        await _storageService.RemoveItem<KeriaConnectionInfo>(StorageArea.Session);
        await _storageService.RemoveItem<ConfigureProgress>(StorageArea.Session);

        // Remove only unproven configs
        await CleanupUnprovenConfigsAsync();

        _logger.LogInformation("ResetAsync: Complete");
        return Result.Ok();
    }

    private async Task<Result<string>> StoreConfigAsync(KeriaConnectConfig config, bool setAsSelected, string? previousDigest = null) {
        // Compute digest for this config
        string digest;
        if (!string.IsNullOrEmpty(config.ClientAidPrefix) && !string.IsNullOrEmpty(config.AgentAidPrefix)) {
            var digestResult = KeriaConnectionDigestHelper.Compute(config);
            if (digestResult.IsFailed) {
                _logger.LogWarning("Could not compute digest: {Errors}", string.Join(", ", digestResult.Errors));
                digest = $"partial_{config.AdminUrl}_{config.PasscodeHash}";
            } else {
                digest = digestResult.Value;
            }
        } else {
            digest = $"partial_{config.AdminUrl}_{config.PasscodeHash}";
        }

        var existingConfigs = await GetConfigsAsync();
        var newDict = new Dictionary<string, KeriaConnectConfig>(existingConfigs.Configs);

        // Remove the previous digest entry if upgrading from partial to complete
        if (previousDigest is not null && previousDigest != digest) {
            newDict.Remove(previousDigest);
        }

        // Remove any stale partial keys when we have a complete digest
        if (!digest.StartsWith("partial_", StringComparison.Ordinal)) {
            var partialKeys = newDict.Keys.Where(k => k.StartsWith("partial_", StringComparison.Ordinal)).ToList();
            foreach (var key in partialKeys) {
                newDict.Remove(key);
            }
        }

        newDict[digest] = config;

        var storeResult = await _storageService.SetItem(existingConfigs with { Configs = newDict });
        if (storeResult.IsFailed) {
            return Result.Fail<string>($"Failed to store KeriaConnectConfigs: {string.Join(", ", storeResult.Errors)}");
        }

        // Update Preferences with selected digest
        if (setAsSelected && !digest.StartsWith("partial_", StringComparison.Ordinal)) {
            var prefsResult = await _storageService.GetItem<Preferences>();
            var currentPrefs = prefsResult.IsSuccess && prefsResult.Value is not null && prefsResult.Value.IsStored
                ? prefsResult.Value
                : new Preferences { IsStored = true };
            var updatedPrefs = currentPrefs with {
                KeriaPreference = currentPrefs.KeriaPreference with {
                    SelectedKeriaConnectionDigest = digest
                }
            };
            var prefsStoreResult = await _storageService.SetItem(updatedPrefs);
            if (prefsStoreResult.IsFailed) {
                _logger.LogWarning("Failed to update SelectedKeriaConnectionDigest: {Errors}", string.Join(", ", prefsStoreResult.Errors));
            }
        }

        return Result.Ok(digest);
    }

    private async Task CleanupUnprovenConfigsAsync() {
        var configs = await GetConfigsAsync();
        var unprovenKeys = configs.Configs
            .Where(kvp => kvp.Value.ProvenAt is null)
            .Select(kvp => kvp.Key)
            .ToList();

        if (unprovenKeys.Count == 0) return;

        var newDict = new Dictionary<string, KeriaConnectConfig>(configs.Configs);
        foreach (var key in unprovenKeys) {
            newDict.Remove(key);
        }

        await _storageService.SetItem(configs with { Configs = newDict });

        // If the selected digest was an unproven config, revert to most recent proven config
        var prefsResult = await _storageService.GetItem<Preferences>();
        if (prefsResult.IsSuccess && prefsResult.Value is not null) {
            var selectedDigest = prefsResult.Value.KeriaPreference.SelectedKeriaConnectionDigest;
            if (selectedDigest is not null && unprovenKeys.Contains(selectedDigest)) {
                var mostRecentProven = newDict
                    .Where(kvp => kvp.Value.ProvenAt is not null)
                    .OrderByDescending(kvp => kvp.Value.ProvenAt)
                    .Select(kvp => kvp.Key)
                    .FirstOrDefault();

                var updatedPrefs = prefsResult.Value with {
                    KeriaPreference = prefsResult.Value.KeriaPreference with {
                        SelectedKeriaConnectionDigest = mostRecentProven
                    }
                };
                await _storageService.SetItem(updatedPrefs);
            }
        }
    }

    private async Task<KeriaConnectConfigs> GetConfigsAsync() {
        var result = await _storageService.GetItem<KeriaConnectConfigs>();
        return result.IsSuccess && result.Value is not null && result.Value.IsStored
            ? result.Value
            : new KeriaConnectConfigs { IsStored = true };
    }

    private async Task<Result<ConfigureResponsePayload>> FailAsync(int step, string message, Result<string>? innerResult = null) {
        var fullMsg = innerResult is not null
            ? $"{message}: {string.Join(", ", innerResult.Errors)}"
            : message;
        _logger.LogError("ConfigureAsync failed at step {Step}: {Error}", step, fullMsg);
        await ReportError(message);
        return Result.Ok(new ConfigureResponsePayload(false, Error: message));
    }

    private async Task<Result<ConfigureResponsePayload>> FailAndCleanupAsync(int step, string message, IReadOnlyList<IError>? errors = null) {
        var fullMsg = errors is not null && errors.Count > 0
            ? $"{message}: {string.Join(", ", errors)}"
            : message;
        _logger.LogError("ConfigureAsync failed at step {Step}: {Error}", step, fullMsg);
        await CleanupUnprovenConfigsAsync();
        await ReportError(message);
        return Result.Ok(new ConfigureResponsePayload(false, Error: message));
    }

    private static string BuildConnectErrorMessage(string errorDetail, ConfigureRequestPayload payload) {
        // Strip common wrapper prefixes to get the root cause message
        var cleaned = errorDetail;
        string[] prefixes = [
            "SignifyClientService: Connect: Exception: ",
            "JavaScript interop failed during Connect: ",
            "Error: "
        ];
        foreach (var prefix in prefixes) {
            if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                cleaned = cleaned[prefix.Length..];
            }
        }

        var lowerCleaned = cleaned.ToLowerInvariant();

        // Detect fetch/network failures that suggest auth-protected boot endpoint
        if (payload.IsNewAccount
            && !string.IsNullOrEmpty(payload.BootUrl)
            && string.IsNullOrEmpty(payload.BootAuthUsername) && string.IsNullOrEmpty(payload.BootAuthPassword)
            && (lowerCleaned.Contains("failed to fetch")
                || lowerCleaned.Contains("401")
                || lowerCleaned.Contains("unauthorized")
                || lowerCleaned.Contains("networkerror")
                || lowerCleaned.Contains("requires authentication"))) {
            return $"Connect failed: {cleaned}. The Boot URL may require Basic Auth credentials.";
        }

        return $"Connect failed: {cleaned}";
    }

    private async Task ReportProgress(int step, int totalSteps, string description) {
        _logger.LogInformation("Configure progress: Step {Step} of {Total}: {Description}", step, totalSteps, description);
        await _storageService.SetItem(new ConfigureProgress {
            Step = step,
            TotalSteps = totalSteps,
            Description = description
        }, StorageArea.Session);
    }

    private async Task ReportComplete() {
        await _storageService.SetItem(new ConfigureProgress { IsComplete = true }, StorageArea.Session);
    }

    private async Task ReportError(string description) {
        await _storageService.SetItem(new ConfigureProgress { IsError = true, Description = description }, StorageArea.Session);
    }
}
