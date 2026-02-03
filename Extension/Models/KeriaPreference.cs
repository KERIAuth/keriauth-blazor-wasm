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
    [JsonPropertyName("SelectedPrefix")]
    public string SelectedPrefix { get; init; } = string.Empty;
}
