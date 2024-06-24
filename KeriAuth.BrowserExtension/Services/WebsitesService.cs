using FluentResults;
using KeriAuth.BrowserExtension.Models;
using System.Diagnostics;

namespace KeriAuth.BrowserExtension.Services;

public class WebsitesService : IWebsitesService

{
    public WebsitesService(IStorageService storageService, ILogger<WebsitesService> logger)
    {
        this.storageService = storageService;
        this.logger = logger;
    }

    private IStorageService storageService;
    private ILogger<WebsitesService> logger;
    private readonly List<IObserver<IEnumerable<Website>>> websitesObservers = [];
    // private IDisposable? stateSubscription;

    public async Task<Result<Websites?>> GetWebsites()
    {
        logger.LogInformation("Getting websites from storage");
        return await storageService.GetItem<Websites>();
    }

    public async Task<Result<Website?>> Get(Uri originUri)
    {
        var res = await GetWebsites();
        if (res.IsFailed)
        {
            logger.LogError("Get: could not fetch websites from storage {websites}", res);
            return Result.Fail("Get: could not fetch websites from storage");
        }
        if (res.Value is null)
        {
            logger.LogError("Get: websites is null");
            return Result.Fail("Get: websites is null");
        }
        var website = res.Value.WebsiteList.Find(w => w.Origin == originUri);
        return Result.Ok(website);
    }

    public async Task<Result> Add(Website website)
    {
        var existingWebsiteResult = await Get(website.Origin);
        if (existingWebsiteResult.IsSuccess)
        {
            return Result.Fail("website already exists");
        }

        var websitesResult = await GetWebsites();
        if (websitesResult.IsFailed)
        {
            logger.LogError("Add: could not fetch websites from storage {1}", websitesResult);
            return Result.Fail("Add: could not fetch websites from storage");
        }

        Debug.Assert (websitesResult.Value is not null, "websitesResult.Value != null");
        // Since Websites is a record, create a new list with the existing websites plus the new one
        var updatedWebsiteList = websitesResult.Value.WebsiteList.Append(website).ToList();

        // Create a new Websites record with the updated list
        var updatedWebsites = new Websites(updatedWebsiteList);

        var saveResult = await storageService.SetItem<Websites>(updatedWebsites);
        if (saveResult.IsFailed)
        {
            return Result.Fail("could not save website to storage");
        }

        return Result.Ok();
    }

    public Task<Result> Delete(Uri originUri)
    {
        throw new NotImplementedException();
    }

    public async Task<Result<Website>> Update(Website updatedWebsite)
    {
        var websitesResult = await GetWebsites();
        if (websitesResult.IsFailed || websitesResult is null || websitesResult.Value is null)
        {
            Debug.Assert(websitesResult is not null, "websitesResult != null");
            logger.LogError("Update: could not fetch websites from storage res: {res}  value: {val}", websitesResult, websitesResult.Value);
            return Result.Fail<Website>("Update: could not fetch websites from storage");
        }

        var existingWebsite = websitesResult.Value.WebsiteList.FirstOrDefault(w => w.Origin == updatedWebsite.Origin);
        if (existingWebsite == null)
        {
            return Result.Fail<Website>("website not found");
        }

        // Since Website is a record, you can't modify it directly. Instead, you create a new list of websites.
        var updatedWebsiteList = websitesResult.Value.WebsiteList
            .Where(w => w.Origin != updatedWebsite.Origin) // Remove the old website
            .Append(updatedWebsite) // Add the updated website
            .ToList();

        var updatedWebsites = new Websites(updatedWebsiteList);

        var saveResult = await storageService.SetItem<Websites>(updatedWebsites);
        if (saveResult.IsFailed)
        {
            return Result.Fail<Website>("could not save updated website to storage");
        }

        return Result.Ok(updatedWebsite);
    }
}

