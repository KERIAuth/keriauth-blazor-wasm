namespace Extension.Services.SignifyService.Models;

using System.Text.Json.Serialization;

public class Notification {
    [JsonPropertyName("i")]
    public required string Id { get; init; }

    [JsonPropertyName("dt")]
    public required string DateTime { get; init; }

    [JsonPropertyName("r")]
    public bool IsRead { get; init; }

    [JsonPropertyName("route")]
    public required string Route { get; init; }

    [JsonPropertyName("exchangeSaid")]
    public string? ExchangeSaid { get; init; }
}
