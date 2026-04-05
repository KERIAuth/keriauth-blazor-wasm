using Extension.Models.Storage;
using System.Text.Json.Serialization;

namespace Extension.Models;

/// <summary>
/// Session-scoped connection info for the active KERIA connection.
/// Stores only the digest reference identifying which KeriaConnectConfig this session is for.
/// Identifiers are stored separately in CachedIdentifiers.
/// Configuration details are retrieved via AppCache computed properties using the digest
/// to look up the full KeriaConnectConfig from KeriaConnectConfigs.
/// </summary>
public record KeriaConnectionInfo : IStorageModel {
    /// <summary>
    /// The KeriaConnectionDigest identifying which KeriaConnectConfig this session is for.
    /// Must match MyPreferences.SelectedKeriaConnectionDigest.
    /// </summary>
    [JsonPropertyName("KeriaConnectionDigest")]
    public required string KeriaConnectionDigest { get; init; }
}
