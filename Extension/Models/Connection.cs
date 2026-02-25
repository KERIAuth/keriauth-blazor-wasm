namespace Extension.Models;

using System.Text.Json.Serialization;

public record Connection {
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("localPrefix")]
    public required string LocalPrefix { get; init; }

    [JsonPropertyName("remotePrefix")]
    public required string RemotePrefix { get; init; }

    [JsonPropertyName("connectionDate")]
    public required DateTime ConnectionDate { get; init; }
}
