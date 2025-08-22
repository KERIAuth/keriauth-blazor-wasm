using FluentResults;
using KeriAuth.BrowserExtension.Models;
using KeriAuth.BrowserExtension.Services.SignifyService;
using KeriAuth.BrowserExtension.Services.SignifyService.Models;

namespace KeriAuth.BrowserExtension.Services
{
    public class IdentifiersService(ILogger<IdentifiersService> logger, IStorageService storageService, ISignifyClientService signifyClientService)
    {
        private readonly Dictionary<string, IdentifierService> identifierServices = [];

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
            logger.Log(ServiceLogLevel, "GetIdentifierHeadlines: Getting identifiers");
            var identifiersRes = await signifyClientService.GetIdentifiers();
            if (identifiersRes is null || identifiersRes.IsFailed)
            {
                var msg = identifiersRes!.Errors.First().Message;
                logger.LogError("GetIdentifierHeadlines: Failed to get identifiers: {msg}", msg);
                return Result.Fail<List<IdentifierHeadline>>(msg);
            }
            else
            {
                logger.Log(ServiceLogLevel, "GetIdentifierHeadlines #: {aids}", identifiersRes.Value.Aids.Count);
                var headlines = new List<IdentifierHeadline>();
                foreach (Aid item in identifiersRes.Value.Aids)
                {
                    // TODO P3  set the current identifierService in the Headline?
                    var identifierService = new IdentifierService(item.Prefix, item.Name, Guid.NewGuid(), logger, storageService);
                    headlines.Add(new IdentifierHeadline(identifierService.GetHeadline().Prefix, identifierService.GetHeadline().Alias, Guid.NewGuid()));
                }
                return Result.Ok(headlines);
            }
        }

        public static LogLevel ServiceLogLevel { get; set; } = LogLevel.Debug;

        public async Task<Result<string>> Add(string alias)
        {
            var res = await signifyClientService.RunCreateAid(alias);
            if (res.IsFailed || res.Value is null)
            {
                logger.LogError("Failed to create person aid: {res}", res.Errors.First().Message);
                return Result.Fail(res.Errors.First().Message);
            }
            else
                return Result.Ok(res.Value);
        }
    }
}
