using Extension.Helper;
using Extension.Models.Storage;
using Extension.Services.SignifyService.Models;
using System.Text.Json.Serialization;

namespace Extension.Models;

public record KeriaConnectionInfo : IStorageModel {
    /// <summary>
    /// UTC timestamp when the current session should expire.
    /// Session expires when current time exceeds this value.
    /// </summary>
    /*
    [JsonPropertyName("SessionExpirationUtc")]
    public required DateTime SessionExpirationUtc { get; init; } = DateTime.MinValue; // important to have an expired default value
    */

    [JsonPropertyName("Config")]
    public required KeriaConnectConfig Config { get; init; }

    [JsonPropertyName("IdentifiersList")]
    public required List<Identifiers> IdentifiersList { get; init; }

    [JsonPropertyName("AgentPrefix")]
    public required string AgentPrefix { get; init; }
}
