namespace Extension.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Represents user preferences for KERIA connection selection.
/// Stored as part of the Preferences model.
/// </summary>
public record KeriaPreference {
    /// <summary>
    /// The KeriaConnectionDigest of the currently selected KERIA configuration.
    /// This digest uniquely identifies a KeriaConnectConfig in the KeriaConnectConfigs collection.
    /// </summary>
    [JsonPropertyName("SelectedKeriaConnectionDigest")]
    public string? SelectedKeriaConnectionDigest { get; init; }

    /// <summary>
    /// The selected AID prefix within the selected KERIA configuration.
    /// </summary>
    /// <remarks>
    /// DEPRECATED: SelectedPrefix is now stored per-config in KeriaConnectConfig.SelectedPrefix.
    /// Use AppCache.SelectedPrefix which reads from the current config.
    /// This property is kept for backward compatibility with existing stored Preferences.
    /// </remarks>
    [Obsolete("SelectedPrefix is now stored in KeriaConnectConfig. Use AppCache.SelectedPrefix instead.")]
    [JsonPropertyName("SelectedPrefix")]
    public string SelectedPrefix { get; init; } = string.Empty;
}
