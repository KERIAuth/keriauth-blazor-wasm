namespace Extension.Tests.Models;

using Extension.Models.Storage;
using Extension.Services.SignifyService.Models;
using System.Text.Json;
using Xunit;

/// <summary>
/// Tests for the CachedNotifications session record.
/// </summary>
public class CachedNotificationsTests {
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void CachedNotifications_DefaultsToEmptyList() {
        var cached = new CachedNotifications();
        Assert.NotNull(cached.Items);
        Assert.Empty(cached.Items);
    }

    [Fact]
    public void CachedNotifications_SerializationRoundTrip() {
        var cached = new CachedNotifications {
            Items = [
                new Notification {
                    Id = "id1",
                    DateTime = "2026-01-01T00:00:00Z",
                    Route = "/exn/ipex/grant",
                    IsRead = false
                }
            ]
        };

        var json = JsonSerializer.Serialize(cached, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<CachedNotifications>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized.Items);
        Assert.Equal("id1", deserialized.Items[0].Id);
    }

    [Fact]
    public void CachedNotifications_IsNotVersioned() {
        var cached = new CachedNotifications();
        Assert.IsNotAssignableFrom<IVersionedStorageModel>(cached);
        Assert.IsAssignableFrom<IStorageModel>(cached);
    }
}
