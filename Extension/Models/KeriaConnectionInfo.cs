using Extension.Services.SignifyService.Models;

namespace Extension.Models;

public record KeriaConnectionInfo(
    KeriaConnectConfig Config,
    Identifiers Identifiers
);