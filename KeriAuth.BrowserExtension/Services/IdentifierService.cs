using KeriAuth.BrowserExtension.Models;

namespace KeriAuth.BrowserExtension.Services
{
    public class IdentifierService
    {
        public IdentifierService(string prefix, string alias, Guid keriaConnectionGuid, ILogger<IdentifiersService> logger, IStorageService storageService)
        {
            _ = prefix;
            _ = storageService;
            _ = logger;
            this.alias = alias;
            this.keriaConnectionGuid = keriaConnectionGuid;
            identifierHeadline = new IdentifierHeadline(prefix, alias, keriaConnectionGuid);
        }

        public readonly IdentifierHeadline identifierHeadline;
        // private readonly string Prefix;
        public readonly string alias;
        public readonly Guid keriaConnectionGuid;

        public IdentifierHeadline Test()
        {
            // logger.LogWarning("IdentifierService: Test() called");
            return identifierHeadline;
        }
    }
}
