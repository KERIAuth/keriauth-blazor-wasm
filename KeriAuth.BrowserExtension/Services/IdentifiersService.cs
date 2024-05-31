using FluentResults;
using KeriAuth.BrowserExtension.Models;
using Microsoft.Extensions.Logging;

namespace KeriAuth.BrowserExtension.Services
{
    public class IdentifiersService
    {
        private readonly ILogger logger;
        private readonly IStorageService storageService;
        private readonly Dictionary<string, IdentifierService> identifierServices = [];

        public IdentifiersService(ILogger<IdentifiersService> logger, IStorageService storageService)
        {
            this.logger = logger;
            this.storageService = storageService;

            // samples
            Guid keriConnectionGuid = new();
            List<(string aid, string alias)> sampleAidAliases = [
                ("0123456789001234567890", "Joe as Member of Jordan School Board"),
                ("asdfghjkzxcvasdfghjkzxcv", "Joe as CFO at XYZ Inc"),
                ("zxcvbnmzxcvbnmzxcvbnmzxcvbnm", "XYZ Inc on X"),
                ("1234asdfzxcv1234asdfzxcv1234", "YoJoe on Discord")
                ];
            foreach (var saa in sampleAidAliases)
            {
                {
                    var identifierService = new IdentifierService(saa.aid, saa.alias, keriConnectionGuid, logger, storageService);
                    identifierServices.Add(saa.aid, identifierService);
                }
            }
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

        public Task<Result<List<IdentifierHeadline>>> GetIdentifierHeadlines()
        {
            var headlines = new List<IdentifierHeadline>();
            foreach (var identifierKey in identifierServices.Keys)
            {
                IdentifierService? identifierService = identifierServices[identifierKey];
                if (identifierService is not null)
                {
                    headlines.Add(new IdentifierHeadline(identifierKey, identifierService.cachedAid.Alias, Guid.NewGuid()));
                }
            }
            return Task.FromResult(Result.Ok(headlines));
        }
    }
}
