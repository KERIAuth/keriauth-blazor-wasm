using FluentResults;
using Extension.Models;
using Extension.Services.Storage;
using System.Text.Json;

namespace Extension.Services;

public class WebsiteConfigService(IStorageGateway storageGateway, ILogger<WebsiteConfigService> logger) : IWebsiteConfigService {

    private async Task<Result<(KeriaConnectConfigs configs, string digest, KeriaConnectConfig config)>> GetCurrentConfigContext() {
        var prefsResult = await storageGateway.GetItem<Preferences>();
        if (prefsResult.IsFailed || prefsResult.Value is null) {
            return Result.Fail("Could not fetch preferences from storage");
        }

        var digest = prefsResult.Value.SelectedKeriaConnectionDigest;
        if (string.IsNullOrEmpty(digest)) {
            return Result.Fail("No KERIA connection selected");
        }

        var configsResult = await storageGateway.GetItem<KeriaConnectConfigs>();
        if (configsResult.IsFailed || configsResult.Value is null) {
            return Result.Fail("Could not fetch KERIA configs from storage");
        }

        if (!configsResult.Value.Configs.TryGetValue(digest, out var config)) {
            return Result.Fail($"No KERIA config found for digest {digest}");
        }

        return Result.Ok((configsResult.Value, digest, config));
    }

    private async Task<Result> SaveWebsiteConfigs(KeriaConnectConfigs configs, string digest, KeriaConnectConfig config, List<WebsiteConfig> updatedWebsiteConfigs) {
        var updatedConfig = config with { WebsiteConfigs = updatedWebsiteConfigs };
        var updatedDict = new Dictionary<string, KeriaConnectConfig>(configs.Configs) {
            [digest] = updatedConfig
        };
        var updatedConfigs = configs with { Configs = updatedDict };
        var saveResult = await storageGateway.SetItem(updatedConfigs);
        if (saveResult.IsFailed) {
            return Result.Fail("Could not save updated KERIA configs to storage");
        }
        return Result.Ok();
    }

    public async Task<Result<List<WebsiteConfig>>> GetList() {
        var ctxResult = await GetCurrentConfigContext();
        if (ctxResult.IsFailed) {
            return Result.Fail(ctxResult.Errors);
        }
        return Result.Ok(ctxResult.Value.config.WebsiteConfigs);
    }

    public async Task<Result> Add(WebsiteConfig website) {
        var ctxResult = await GetCurrentConfigContext();
        if (ctxResult.IsFailed) {
            return Result.Fail(ctxResult.Errors);
        }

        var (configs, digest, config) = ctxResult.Value;

        if (config.WebsiteConfigs.Any(w => w.Origin == website.Origin)) {
            return Result.Fail("website already exists");
        }

        var updatedList = config.WebsiteConfigs.Append(website).ToList();
        return await SaveWebsiteConfigs(configs, digest, config, updatedList);
    }

    public async Task<Result> Delete(Uri originUri) {
        var ctxResult = await GetCurrentConfigContext();
        if (ctxResult.IsFailed) {
            return Result.Fail(ctxResult.Errors);
        }

        var (configs, digest, config) = ctxResult.Value;
        var updatedList = config.WebsiteConfigs.Where(w => w.Origin != originUri).ToList();
        return await SaveWebsiteConfigs(configs, digest, config, updatedList);
    }

    public async Task<Result> Update(WebsiteConfig websiteConfig) {
        var ctxResult = await GetCurrentConfigContext();
        if (ctxResult.IsFailed) {
            return Result.Fail(ctxResult.Errors);
        }

        var (configs, digest, config) = ctxResult.Value;
        var updatedList = config.WebsiteConfigs
            .Select(w => w.Origin == websiteConfig.Origin ? websiteConfig : w)
            .ToList();

        var saveResult = await SaveWebsiteConfigs(configs, digest, config, updatedList);
        if (saveResult.IsSuccess) {
            logger.LogInformation(nameof(Update) + ": Updated websiteConfig {website}", JsonSerializer.Serialize(websiteConfig));
        }
        return saveResult;
    }

    public async Task<Result<(WebsiteConfig websiteConfig1, bool isConfigNew)>> GetOrCreateWebsiteConfig(Uri originUri) {
        var ctxResult = await GetCurrentConfigContext();
        if (ctxResult.IsFailed) {
            return Result.Fail(ctxResult.Errors);
        }

        var (configs, digest, config) = ctxResult.Value;
        var existing = config.WebsiteConfigs.FirstOrDefault(w => w.Origin == originUri);
        if (existing is not null) {
            return Result.Ok((existing, false));
        }

        logger.LogInformation(nameof(GetOrCreateWebsiteConfig) + ": Adding websiteConfig for {originUri}", originUri);
        var newWebsiteConfig = new WebsiteConfig(originUri, [], null, null, false, false, false);
        var updatedList = config.WebsiteConfigs.Append(newWebsiteConfig).ToList();

        var saveResult = await SaveWebsiteConfigs(configs, digest, config, updatedList);
        if (saveResult.IsFailed) {
            return Result.Fail(saveResult.Errors);
        }

        logger.LogInformation(nameof(GetOrCreateWebsiteConfig) + ": Added website to database");
        return Result.Ok((newWebsiteConfig, true));
    }
}
