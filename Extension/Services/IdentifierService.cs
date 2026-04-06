namespace Extension.Services;

using Extension.Services.Storage;

public class IdentifierService {
    public IdentifierService(string prefix, string alias, Guid keriaConnectionGuid, ILogger<IdentifierService> logger) {
            Prefix = prefix;
            Alias = alias;
            KeriaConnectionGuid = keriaConnectionGuid;
            _ = logger;
        }

        public string Prefix { get; }
        public string Alias { get; }
        public Guid KeriaConnectionGuid { get; }
}
