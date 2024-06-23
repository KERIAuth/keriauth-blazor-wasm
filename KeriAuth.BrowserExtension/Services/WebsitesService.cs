using FluentResults;
using KeriAuth.BrowserExtension.Models;

namespace KeriAuth.BrowserExtension.Services;

public class WebsitesService : IWebsitesService

{
    public WebsitesService(IStorageService storageService, ILogger<PreferencesService> logger)
    {
        this.storageService = storageService;
        this.logger = logger;
    }

    private IStorageService storageService;
    private ILogger<PreferencesService> logger;
    private readonly List<IObserver<IEnumerable<Website>>> websitesObservers = [];
    // private IDisposable? stateSubscription;

    public async Task<Result<Websites?>> GetWebsites()
    {
        logger.LogInformation("Getting websites from storage");
        return (await storageService.GetItem<Websites>()).ToResult();
    }

    public async Task<Result<Website>> Get(Uri originUri)
    {
        var websites = await GetWebsites();
        if (websites.IsFailed)
        {
            return Result.Fail("could not fetch websites from storage");
        }
        var website = websites.Value.WebsiteList.Find(w => w.Origin == originUri);
        return website is not null ? Result.Ok(website) : Result.Fail("website not found");
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
            return Result.Fail("could not fetch websites from storage");
        }

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
            return Result.Fail<Website>("could not fetch websites from storage");
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

