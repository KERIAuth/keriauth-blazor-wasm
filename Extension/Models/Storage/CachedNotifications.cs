namespace Extension.Models.Storage;

using System.Text.Json.Serialization;
using Extension.Services.SignifyService.Models;

/// <summary>
/// Session storage cache of notifications fetched from KERIA.
/// Written proactively by NotificationPollingService.
/// </summary>
public record CachedNotifications : IStorageModel {
    [JsonPropertyName("items")]
    public List<Notification> Items { get; init; } = [];
}
