using Extension.Models.Storage;
using Extension.Services.SignifyService.Models;
using System.Text.Json.Serialization;

namespace Extension.Models;

/// <summary>
/// Session-scoped connection info for the active KERIA connection.
/// Stores only the digest reference and session-specific data (IdentifiersList).
/// Configuration details are retrieved via AppCache computed properties using the digest
/// to look up the full KeriaConnectConfig from KeriaConnectConfigs.
/// </summary>
public record KeriaConnectionInfo : IStorageModel {
    /// <summary>
    /// The KeriaConnectionDigest identifying which KeriaConnectConfig this session is for.
    /// Must match MyPreferences.KeriaPreference.SelectedKeriaConnectionDigest.
    /// </summary>
    [JsonPropertyName("KeriaConnectionDigest")]
    public required string KeriaConnectionDigest { get; init; }

    /// <summary>
    /// List of identifiers (AIDs) fetched from KERIA for this session.
    /// </summary>
    [JsonPropertyName("IdentifiersList")]
    public required List<Identifiers> IdentifiersList { get; init; }
}
