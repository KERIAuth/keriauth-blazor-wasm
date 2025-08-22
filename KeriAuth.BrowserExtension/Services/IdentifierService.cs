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

        private readonly IdentifierHeadline identifierHeadline;
        // private readonly string Prefix;
        private readonly string alias;
        private readonly Guid keriaConnectionGuid;

        public IdentifierHeadline GetHeadline() => identifierHeadline; 

        public IdentifierHeadline Test()
        {
            // logger.LogWarning("IdentifierService: Test() called");
            return identifierHeadline;
        }
    }
}
