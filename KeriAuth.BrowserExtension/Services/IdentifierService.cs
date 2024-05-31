using KeriAuth.BrowserExtension.Models;

namespace KeriAuth.BrowserExtension.Services
{
    public class IdentifierService
    {
        public IdentifierService(string prefix, string alias, Guid keriaConnectionGuid, ILogger<IdentifiersService> logger, IStorageService storageService)
        {
            this.prefix = prefix;
            this.logger = logger;
            this.storageService = storageService;
            this.alias = alias;
            this.keriaConnectionGuid = keriaConnectionGuid;
            cachedAid = new CachedAid(prefix, alias, keriaConnectionGuid);
        }

        // cachedAids.Add(new CachedAid("0123456789abcdefghij", "Johnny as Founder at J Foundation", "localhosts"));
        // cachedAids.Add(new CachedAid("qwerqwerqwerqwerqwerqwerqwerqwer", "J Foundation on X", "localhosts"));
        // cachedAids.Add(new CachedAid("ertywertwertwertwertwertwertwert", "J. Suhr as Partner at Suhr Consortium", "localhost2"));


        public readonly CachedAid cachedAid;
        private readonly string prefix;
        public readonly string alias;
        public readonly Guid keriaConnectionGuid;

        private readonly ILogger<IdentifiersService> logger;
        private readonly IStorageService storageService;

        public CachedAid Test()
        {
            // logger.LogWarning("IdentifierService: Test() called");
            return cachedAid;
        }
    }
}
