namespace Extension.Models.Storage;

/// <summary>
/// Maps Local storage model type names to their expected SchemaVersion.
/// Used by StorageService.GetItem to detect stale records after breaking schema changes.
/// Only bump a version when the schema has a breaking change (restructuring, field removal, type change).
/// Additive changes (new optional fields with defaults) do not require a version bump.
/// </summary>
public static class StorageModelRegistry {
    private static readonly Dictionary<string, int> ExpectedVersions = new() {
        [nameof(Preferences)] = 2,
        [nameof(KeriaConnectConfigs)] = 2,
        [nameof(OnboardState)] = 1,
        [nameof(WebsiteConfigList)] = 1,
    };

    /// <summary>
    /// Returns the expected SchemaVersion for a given type name, or null if not registered.
    /// </summary>
    public static int? GetExpectedVersion(string typeName) {
        return ExpectedVersions.TryGetValue(typeName, out var version) ? version : null;
    }
}
