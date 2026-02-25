namespace Extension.Models.Storage;

using System.Text.Json.Serialization;
using Extension.Services.SignifyService.Models;

public record Notifications : IStorageModel {
    [JsonPropertyName("items")]
    public List<Notification> Items { get; init; } = [];
}
