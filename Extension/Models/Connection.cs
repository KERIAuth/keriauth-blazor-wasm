namespace Extension.Models;

using System.Text.Json.Serialization;

public record Connection {
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("senderPrefix")]
    public required string SenderPrefix { get; init; }

    [JsonPropertyName("receiverPrefix")]
    public required string ReceiverPrefix { get; init; }

    [JsonPropertyName("connectionDate")]
    public required DateTime ConnectionDate { get; init; }
}
