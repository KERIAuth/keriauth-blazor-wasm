namespace Extension.Models.Storage;

using System.Text.Json.Serialization;
using Extension.Services.SignifyService.Models;

/// <summary>
/// Session storage record for identifiers (AIDs) fetched from KERIA.
/// Separated from KeriaConnectionInfo for clarity — everything in Session
/// is for one connection, and having identifiers separate improves understandability.
/// </summary>
public record CachedIdentifiers : IStorageModel {
    [JsonPropertyName("IdentifiersList")]
    public required List<Identifiers> IdentifiersList { get; init; }
}
