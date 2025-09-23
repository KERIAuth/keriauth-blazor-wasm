using Extension.Models;

namespace Extension.Services {
    public class IdentifierService {
        public IdentifierService(string prefix, string alias, Guid keriaConnectionGuid, ILogger<IdentifiersService> logger, IStorageService storageService) {
            _ = prefix;
            _ = storageService;
            _ = logger;
            _ = alias;
            _ = keriaConnectionGuid;
            identifierHeadline = new IdentifierHeadline(prefix, alias, keriaConnectionGuid);
        }

        private readonly IdentifierHeadline identifierHeadline;
               
        public IdentifierHeadline GetHeadline() => identifierHeadline;
    }
}
