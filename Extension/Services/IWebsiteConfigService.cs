using FluentResults;
using Extension.Models;

namespace Extension.Services {
    public interface IWebsiteConfigService {
        Task<Result<List<WebsiteConfig>>> GetList();

        Task<Result> Add(WebsiteConfig website);

        Task<Result> Delete(Uri originUri);

        Task<Result> Update(WebsiteConfig websiteConfig);

        Task<Result<(WebsiteConfig websiteConfig1, bool isConfigNew)>> GetOrCreateWebsiteConfig(Uri originUri);
    }
}
