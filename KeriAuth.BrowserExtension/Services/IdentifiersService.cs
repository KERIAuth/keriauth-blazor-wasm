using FluentResults;
using KeriAuth.BrowserExtension.Models;
using KeriAuth.BrowserExtension.Services.SignifyService.Models;
using KeriAuth.BrowserExtension.Services.SignifyService;
using Microsoft.Extensions.Logging;

namespace KeriAuth.BrowserExtension.Services
{
    public class IdentifiersService
    {
        private readonly ILogger<IdentifiersService> logger;
        private readonly IStorageService storageService;
        private readonly Dictionary<string, IdentifierService> identifierServices = [];
        private readonly ISignifyClientService signifyClientService;

        public IdentifiersService(ILogger<IdentifiersService> logger, IStorageService storageService, ISignifyClientService signifyClientService)
        {
            this.logger = logger;
            this.storageService = storageService;
            this.signifyClientService = signifyClientService;
        }

        public Task<Result<IdentifierService>> GetIdentifierService(string prefix)
        {
            identifierServices.TryGetValue(prefix, out IdentifierService? identifierService);
            if (identifierService == null)
            {
                return Task.FromResult(Result.Fail<IdentifierService>("Identifier service not found"));
            }
            return Task.FromResult(Result.Ok(identifierService));
        }

        public async Task<Result<List<IdentifierHeadline>>> GetIdentifierHeadlines()
        {
            logger.LogWarning("GetIdentifierHeadlines: Getting identifiers");
            var res2 = await signifyClientService.GetIdentifiers();
            if (res2 is null || res2.IsFailed)
            {
                var msg = res2!.Errors.First().Message;
                logger.LogError("GetIdentifierHeadlines: Failed to get identifiers: {msg}", msg);
                return Result.Fail<List<IdentifierHeadline>>(msg);
            }
            if (res2.Value is not null && res2.IsSuccess)
            {
                logger.LogWarning("GetIdentifierHeadlines: {aids}", res2.Value.Aids.Count());
                var headlines = new List<IdentifierHeadline>();
                
                foreach (Aid item in res2.Value.Aids)
                {
                    // TODO ??  set the current identifierService in the Headlin?
                    var identifierService = new IdentifierService(item.Prefix, item.Name, Guid.NewGuid(), logger, storageService);
                    headlines.Add(new IdentifierHeadline(item.Prefix, identifierService.cachedAid.Alias, Guid.NewGuid()));
                }
                return Result.Ok(headlines);
            }
            return Result.Fail<List<IdentifierHeadline>>("Failed to get identifiers");
        }
    }
}
