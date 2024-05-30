using FluentResults;
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

            // TODO remove
            // var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

            var prefixes = new string[] { "11111", "22222", "33", "44444" };
            foreach (string prefix in prefixes)             {
                identifierServices.Add(prefix,
                    new IdentifierService(prefix, logger, storageService)
                    );
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
    }
}
