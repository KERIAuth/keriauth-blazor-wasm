namespace Extension.Models.Storage;

/// <summary>
/// Enterprise policy configuration set by IT administrators via Managed storage.
/// Extensions can only read this configuration - it is set via Chrome Enterprise Policy.
/// See: https://support.google.com/chrome/a/answer/9296680
///
/// Storage key: "EnterprisePolicyConfig"
/// Storage area: Managed (read-only)
/// Configured by: IT admins via Group Policy or registry
///
/// Example IT policy JSON:
/// {
///   "3rdparty": {
///     "extensions": {
///       "YOUR_EXTENSION_ID": {
///         "EnterprisePolicyConfig": {
///           "KeriaAdminUrl": "https://keria.company.com/admin",
///           "KeriaBootUrl": "https://keria.company.com/boot"
///         }
///       }
///     }
///   }
/// }
/// </summary>
public record EnterprisePolicyConfig : IStorageModel {
    /// <summary>
    /// Required KERIA Admin URL configured by IT.
    /// If set, extension will only connect to this KERIA admin endpoint.
    /// Null means no IT-enforced URL.
    /// </summary>
    public string? KeriaAdminUrl { get; init; }

    /// <summary>
    /// Required KERIA Boot URL configured by IT.
    /// If set, extension will only connect to this KERIA boot endpoint.
    /// Null means no IT-enforced URL.
    /// </summary>
    public string? KeriaBootUrl { get; init; }

    /// <summary>
    /// UTC timestamp when IT last updated this policy.
    /// Useful for auditing when policy changes were applied.
    /// Null if IT did not provide timestamp.
    /// </summary>
    public DateTime? UpdatedUtc { get; init; }
}
