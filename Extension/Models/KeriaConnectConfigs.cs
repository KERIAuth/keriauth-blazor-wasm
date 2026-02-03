namespace Extension.Models;

using System.Text.Json.Serialization;
using Extension.Models.Storage;

/// <summary>
/// Storage model for multiple KERIA Cloud Service configurations.
/// Each configuration is keyed by its computed KeriaConnectionDigest.
/// </summary>
public record KeriaConnectConfigs : IStorageModel {
    /// <summary>
    /// Dictionary of KeriaConnectConfig items keyed by their computed KeriaConnectionDigest.
    /// The digest is computed as SHA256(ClientAidPrefix + AgentAidPrefix + PasscodeHash).
    /// </summary>
    [JsonPropertyName("Configs")]
    public Dictionary<string, KeriaConnectConfig> Configs { get; init; } = new();

    [JsonPropertyName("IsStored")]
    public bool IsStored { get; init; }
}
