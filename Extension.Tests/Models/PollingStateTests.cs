namespace Extension.Tests.Models;

using Extension.Models.Storage;
using System.Text.Json;
using Xunit;

/// <summary>
/// Tests for the PollingState session record.
/// </summary>
public class PollingStateTests {
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void PollingState_DefaultsToNullTimestamps() {
        var state = new PollingState();
        Assert.Null(state.IdentifiersLastFetchedUtc);
        Assert.Null(state.CredentialsLastFetchedUtc);
        Assert.Null(state.NotificationsLastFetchedUtc);
    }

    [Fact]
    public void PollingState_SerializationRoundTrip() {
        var now = DateTime.UtcNow;
        var state = new PollingState {
            IdentifiersLastFetchedUtc = now.AddMinutes(-1),
            CredentialsLastFetchedUtc = now.AddMinutes(-2),
            NotificationsLastFetchedUtc = now.AddMinutes(-3)
        };

        var json = JsonSerializer.Serialize(state, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<PollingState>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.IdentifiersLastFetchedUtc);
        Assert.NotNull(deserialized.CredentialsLastFetchedUtc);
        Assert.NotNull(deserialized.NotificationsLastFetchedUtc);
    }

    [Fact]
    public void PollingState_WithExpression_UpdatesSingleField() {
        var state = new PollingState {
            IdentifiersLastFetchedUtc = DateTime.UtcNow.AddMinutes(-5)
        };

        var now = DateTime.UtcNow;
        var updated = state with { CredentialsLastFetchedUtc = now };

        // Original field preserved, new field set
        Assert.NotNull(updated.IdentifiersLastFetchedUtc);
        Assert.Equal(now, updated.CredentialsLastFetchedUtc);
        Assert.Null(updated.NotificationsLastFetchedUtc);
    }

    [Fact]
    public void PollingState_IsNotVersioned() {
        var state = new PollingState();
        Assert.IsNotAssignableFrom<IVersionedStorageModel>(state);
        Assert.IsAssignableFrom<IStorageModel>(state);
    }
}
