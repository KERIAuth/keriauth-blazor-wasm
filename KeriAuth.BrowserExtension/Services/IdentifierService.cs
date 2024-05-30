using KeriAuth.BrowserExtension.Models;

namespace KeriAuth.BrowserExtension.Services
{
    public class IdentifierService
    {
        public IdentifierService(string prefix, ILogger<IdentifiersService> logger, IStorageService storageService)
        {
            this.prefix = prefix;
            this.logger = logger;
            this.storageService = storageService;
            identifier = new CachedAid(prefix, "alias1234", "localhost1234");
        }

        private readonly CachedAid identifier;
        private readonly string prefix;
        private readonly ILogger<IdentifiersService> logger;
        private readonly IStorageService storageService;

        public CachedAid Test()
        {
            // logger.LogWarning("IdentifierService: Test() called");
            return identifier;
        }
    }
}
