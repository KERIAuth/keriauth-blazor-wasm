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
            var res2 = await signifyClientService.GetIdentifiers();
            if (res2 is null || res2.IsFailed)
            {
                var msg = res2!.Errors.First().Message;
                logger.LogError("GetIdentifierHeadlines: Failed to get identifiers: {msg}", msg);
                return Result.Fail<List<IdentifierHeadline>>(msg);
            }
            if (res2.Value is not null && res2.IsSuccess)
            {
                logger.Log(ServiceLogLevel, "GetIdentifierHeadlines #: {aids}", res2.Value.Aids.Count);
                var headlines = new List<IdentifierHeadline>();

                foreach (Aid item in res2.Value.Aids)
                {
                    //// TMP
                    //var x = await Signify_ts_shim.GetAID(item.Name);
                    //if (x is null )
                    //{
                    //    logger.LogError("Failed to get AID for prefix: {name}", item.Name);
                    //    continue;
                    //}
                    //logger.LogWarning("GetIdentifierHeadlines: GetAID test {1}", x);


                    //var y = await signifyClientService.GetIdentifier(item.Name);
                    //if (y.IsFailed)
                    //{
                    //    logger.LogError("Failed to get IdentifierService for prefix: {name}", item.Name);
                    //    continue;
                    //}
                    //var aid = y.Value;
                    //// logger.LogWarning("GetIdentifierHeadlines: GetIdentifierService object {1}", (object)aid);
                    //logger.LogWarning("GetIdentifierHeadlines: GetIdentifierService name {1}", aid.Name);
                    //logger.LogWarning("GetIdentifierHeadlines: GetIdentifierService prefix {1}", aid.Prefix);
                    //// logger.LogWarning("GetIdentifierHeadlines: GetIdentifierService state {1}", (object)aid.State);
                    //// logger.LogWarning("GetIdentifierHeadlines: GetIdentifierService salty {1}", (object)aid.Salty);
                    //logger.LogWarning("GetIdentifierHeadlines: GetIdentifierService transferable {1}", aid.Transferable);
                    //// logger.LogWarning("GetIdentifierHeadlines: GetIdentifierService windexes {1}", (object)aid.Windexes);
                    //// TODO !! parse?


                    // TODO ??  set the current identifierService in the Headline?
                    var identifierService = new IdentifierService(item.Prefix, item.Name, Guid.NewGuid(), logger, storageService);
                    headlines.Add(new IdentifierHeadline(item.Prefix, identifierService.cachedAid.Alias, Guid.NewGuid()));
                }
                return Result.Ok(headlines);
            }
            return Result.Fail<List<IdentifierHeadline>>("Failed to get identifiers");
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
