using FluentResults;
using KeriAuth.BrowserExtension.Models;
using System.Diagnostics;
using System.Text.Json;

namespace KeriAuth.BrowserExtension.Services;

public class WebsiteConfigService(IStorageService storageService, ILogger<WebsiteConfigService> logger) : IWebsiteConfigService
{
    public async Task<Result<WebsiteConfigList?>> GetList()
    {
        // logger.LogInformation("Getting websites from storage");
        return await storageService.GetItem<WebsiteConfigList>();
    }

    private async Task<Result<WebsiteConfig?>> Get(Uri originUri)
    {
        var res = await GetList();
        if (res.IsFailed)
        {
            // logger.LogError("Get: could not fetch websites from storage {websites}", res);
            return Result.Fail("Get: could not fetch websites from storage");
        }
        if (res.Value is null)
        {
            // logger.LogError("Get: websites is null");
            return Result.Fail("Get: websites is null");
        }
        var website = res.Value.WebsiteList.Find(w => w.Origin == originUri);
        return Result.Ok(website);
    }

    public async Task<Result> Add(WebsiteConfig website)
    {
        var existingWebsiteResult = await Get(website.Origin);
        if (existingWebsiteResult.IsSuccess)
        {
            return Result.Fail("website already exists");
        }

        var websitesResult = await GetList();
        if (websitesResult.IsFailed)
        {
            // logger.LogError("Add: could not fetch websites from storage {res}", websitesResult);
            return Result.Fail("Add: could not fetch websites from storage");
        }

        Debug.Assert(websitesResult.Value is not null, "websitesResult.Value != null");
        // Since Websites is a record, create a new list with the existing websites plus the new one
        var updatedWebsiteList = websitesResult.Value.WebsiteList.Append(website).ToList();

        // Create a new Websites record with the updated list
        var updatedWebsites = new WebsiteConfigList(updatedWebsiteList);

        var saveResult = await storageService.SetItem<WebsiteConfigList>(updatedWebsites);
        if (saveResult.IsFailed)
        {
            return Result.Fail("could not save website to storage");
        }

        return Result.Ok();
    }

    public async Task<Result> Delete(Uri originUri)
    {
        var websitesResult = await GetList();
        if (websitesResult.IsFailed || websitesResult is null || websitesResult.Value is null)
        {
            Debug.Assert(websitesResult is not null, "websitesResult != null");
            // logger.LogError("Update: could not fetch websites from storage res: {res}  value: {val}", websitesResult, websitesResult.Value);
            return Result.Fail("Update: could not fetch websites from storage");
        }

        var newWebsites = websitesResult.Value.WebsiteList
            .Where((ww) => ww.Origin != originUri);

        var wcl = new WebsiteConfigList(new List<WebsiteConfig>(newWebsites));

        var saveResult = await storageService.SetItem<WebsiteConfigList>(wcl);
        if (saveResult.IsFailed)
        {
            return Result.Fail("could not save updated websites to storage");
        }

        // logger.LogInformation("Updated websiteConfig {website}", JsonSerializer.Serialize(wcl));
        return Result.Ok();
    }

    public async Task<Result> Update(WebsiteConfig updatedWebsiteConfig)
    {
        var websitesResult = await GetList();
        if (websitesResult.IsFailed || websitesResult is null || websitesResult.Value is null)
        {
            Debug.Assert(websitesResult is not null, "websitesResult != null");
            // logger.LogError("Update: could not fetch websites from storage res: {res}  value: {val}", websitesResult, websitesResult.Value);
            return Result.Fail("Update: could not fetch websites from storage");
        }

        var existingWebsiteConfigOrNothing = websitesResult.Value.WebsiteList.First(w => w.Origin == updatedWebsiteConfig.Origin) ?? updatedWebsiteConfig;
        WebsiteConfigList newList = new([]);

        foreach (var config in websitesResult.Value.WebsiteList)
        {
            if (config.Origin == updatedWebsiteConfig.Origin)
            {
                newList.WebsiteList.Add(updatedWebsiteConfig);
            }
            else
            {
                var newConfig = config with { IsAutoSignSafeHeaders = true };
                newList.WebsiteList.Add(newConfig);
            }
        }

        /*
        // Since Website is a record, you can't modify it directly. Instead, you create a new list of websites.
        var updatedWebsiteList = websitesResult.Value.WebsiteList
            .Where(w => w.Origin != updatedWebsiteConfig.Origin) // Remove the old websiteConfig
            .Append(updatedWebsiteConfig) // Add the updated websiteConfig
            .ToList();

        var saveResult = await storageService.SetItem<WebsiteConfigList>(new WebsiteConfigList(updatedWebsiteList));
        */
        var saveResult = await storageService.SetItem<WebsiteConfigList>(newList);
        if (saveResult.IsFailed)
        {
            return Result.Fail("could not save updated website to storage");
        }

        logger.LogInformation("Updated websiteConfig {website}", JsonSerializer.Serialize(updatedWebsiteConfig));
        return Result.Ok();
    }

    public async Task<Result<(WebsiteConfig websiteConfig1, bool isConfigNew)>> GetOrCreateWebsiteConfig(Uri originUri)
    {
        // logger.LogInformation("GetOrCreateWebsiteConfig Uri {uri}", originUri);
        WebsiteConfigList websiteConfigList;
        var getWebsitesRes = await GetList();
        if (getWebsitesRes is null || getWebsitesRes.IsFailed)
        {
            // logger.LogError("Error in websiteService {err}", getWebsitesRes?.Errors);
            return Result.Fail(error: getWebsitesRes?.Errors[0]);
        }
        else
        {
            // logger.LogInformation("getOrCreateWebsiteConfig: from storage: {res}", JsonSerializer.Serialize(getWebsitesRes));

            if (getWebsitesRes.Value is null)
            {
                // This is the first website configured. Need to first add the Websites collection
                // var websiteConfig = (new WebsiteConfig(originUri, [], null, null, false, false, false)).Validate();
                websiteConfigList = new WebsiteConfigList(WebsiteList: [new WebsiteConfig(originUri, [], null, null, false, false, true)]);
                var setItemRes = await storageService.SetItem<WebsiteConfigList>(websiteConfigList);
                if (setItemRes.IsFailed)
                {
                    // logger.LogError("getOrCreateWebsite: Error adding websites to database: {err}", setItemRes.Errors);
                    return Result.Fail(error: setItemRes.Errors[0]);
                }
                else
                {
                    // logger.LogInformation("Added websites to database");
                }
            }
            else
            {
                websiteConfigList = getWebsitesRes.Value;
            }

            // Find the website in the collection
            var websiteConfigOrNothing = websiteConfigList.WebsiteList.FirstOrDefault<WebsiteConfig>(a => a.Origin == originUri);
            // logger.LogInformation("getOrCreateWebsite: websiteConfig for {origin}: {websiteConfig}", originUri, websiteConfigOrNothing);
            if (websiteConfigOrNothing is null)
            {
                logger.LogInformation("Adding websiteConfig for {originUri}", originUri);
                WebsiteConfig newWebsiteConfig = new(originUri, [], null, null, false, false, true);
                // newWebsiteConfig.Validate();
                websiteConfigList.WebsiteList.Add(newWebsiteConfig);
                var setItemRes = await storageService.SetItem<WebsiteConfigList>(websiteConfigList);
                if (setItemRes.IsFailed)
                {
                    // logger.LogError("getOrCreateWebsite: Error adding website to database: {err}", setItemRes.Errors);
                    return Result.Fail(error: setItemRes.Errors[0]);
                }
                else
                {
                    logger.LogInformation("Added website to database");
                }
                return Result.Ok((newWebsiteConfig, true));
            }
            else
            {
                WebsiteConfig website = websiteConfigOrNothing;
                return Result.Ok((website, false));
            }
        }
    }
}

