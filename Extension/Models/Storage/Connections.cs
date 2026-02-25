namespace Extension.Models.Storage;

using System.Text.Json.Serialization;

public record Connections : IStorageModel {
    [JsonPropertyName("items")]
    public List<Connection> Items { get; init; } = [];
}
