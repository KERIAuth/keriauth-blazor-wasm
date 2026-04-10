namespace Extension.Services.SignifyBroker;

/// <summary>
/// Identifies the signify-ts operation being brokered.
/// Used for logging, diagnostics, and reachability tracking.
/// </summary>
public enum SignifyOperation {
    Connect,
    Disconnect,
    GetState,
    TestAsync,
    HealthCheck,

    // Identifiers
    GetIdentifiers,
    CreateAidWithEndRole,
    RenameAid,

    // Credentials
    GetCredentials,
    GetCredentialsRaw,
    IssueCredential,
    IssueAndGetCredential,

    // Registries
    ListRegistries,
    CreateRegistryIfNotExists,

    // IPEX
    IpexOfferAndSubmit,
    IpexAdmitAndSubmit,
    IpexAgreeAndSubmit,
    IpexApplyAndSubmit,
    GrantWithElidedAcdc,

    // OOBI
    GetOobi,
    ResolveOobi,

    // Schema
    GetSchemaRaw,

    // Exchange
    GetExchangeRaw,

    // Operations
    WaitForOperation,

    // Key State
    GetKeyState,
    GetKeyEvents,

    // Notifications
    ListNotifications,
    MarkNotification,
    DeleteNotification,
}
