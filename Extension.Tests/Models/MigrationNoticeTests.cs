namespace Extension.Tests.Models;

using Extension.Models.Storage;
using System.Text.Json;
using Xunit;

/// <summary>
/// Tests for the MigrationNotice local storage record.
/// </summary>
public class MigrationNoticeTests {
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void MigrationNotice_DefaultsToEmptyList() {
        var notice = new MigrationNotice();
        Assert.NotNull(notice.DiscardedTypeNames);
        Assert.Empty(notice.DiscardedTypeNames);
    }

    [Fact]
    public void MigrationNotice_SerializationRoundTrip() {
        var notice = new MigrationNotice {
            DiscardedTypeNames = ["Preferences", "KeriaConnectConfigs"]
        };

        var json = JsonSerializer.Serialize(notice, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<MigrationNotice>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.DiscardedTypeNames.Count);
        Assert.Contains("Preferences", deserialized.DiscardedTypeNames);
        Assert.Contains("KeriaConnectConfigs", deserialized.DiscardedTypeNames);
    }

    [Fact]
    public void MigrationNotice_IsNotVersioned() {
        // MigrationNotice intentionally does not implement IVersionedStorageModel
        // so it is not subject to the version-check-discard logic it itself reports on.
        var notice = new MigrationNotice();
        Assert.IsNotAssignableFrom<IVersionedStorageModel>(notice);
        Assert.IsAssignableFrom<IStorageModel>(notice);
    }

    [Fact]
    public void MigrationNotice_NotInStorageModelRegistry() {
        // Ensure MigrationNotice is excluded from the version registry — it's unversioned.
        Assert.Null(StorageModelRegistry.GetExpectedVersion(nameof(MigrationNotice)));
    }
}
