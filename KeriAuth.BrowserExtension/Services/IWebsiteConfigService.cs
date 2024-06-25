using System.Collections.Generic;
using FluentAssertions;
using FluentResults;
using KeriAuth.BrowserExtension.Models;

namespace KeriAuth.BrowserExtension.Services
{
    public interface IWebsiteConfigService
    {
        Task<Result<WebsiteConfigList?>> GetList();

        Task<Result> Add(WebsiteConfig website);

        Task<Result> Delete(Uri originUri);

        Task<Result> Update(WebsiteConfig website);

        Task<Result<WebsiteConfig>> GetOrCreateWebsiteConfig(Uri originUri);
    }
}
