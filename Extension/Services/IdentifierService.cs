namespace Extension.Services;

using Extension.Services.Storage;

public class IdentifierService {
    public IdentifierService(string prefix, string alias, Guid keriaConnectionGuid, ILogger<IdentifierService> logger, IStorageService storageService) {
            Prefix = prefix;
            Alias = alias;
            KeriaConnectionGuid = keriaConnectionGuid;
            _ = storageService;
            _ = logger;
        }

        public string Prefix { get; }
        public string Alias { get; }
        public Guid KeriaConnectionGuid { get; }
}
