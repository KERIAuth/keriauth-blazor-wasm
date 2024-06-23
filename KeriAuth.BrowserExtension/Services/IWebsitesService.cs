using System.Collections.Generic;
using FluentAssertions;
using FluentResults;
using KeriAuth.BrowserExtension.Models;

namespace KeriAuth.BrowserExtension.Services
{
    public interface IWebsitesService // TODO : IObservable<IEnumerable<Website>>
    {
        Task<Result<Websites?>> GetWebsites();

        Task<Result<Website?>> Get(Uri originUri);

        Task<Result> Add(Website website);

        Task<Result> Delete(Uri originUri);

        Task<Result<Website>> Update(Website website);
    }
}
