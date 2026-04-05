namespace Extension.Tests.Models;

using Extension.Models;
using Extension.Models.Storage;
using Xunit;

/// <summary>
/// Tests for the schema versioning system on Local storage records.
/// </summary>
public class StorageModelVersionTests {

    [Fact]
    public void StorageModelRegistry_ContainsAllLocalRecords() {
        // All Local storage records should have a registry entry
        Assert.NotNull(StorageModelRegistry.GetExpectedVersion(nameof(Preferences)));
        Assert.NotNull(StorageModelRegistry.GetExpectedVersion(nameof(KeriaConnectConfigs)));
        Assert.NotNull(StorageModelRegistry.GetExpectedVersion(nameof(OnboardState)));
        Assert.NotNull(StorageModelRegistry.GetExpectedVersion(nameof(WebsiteConfigList)));
    }

    [Fact]
    public void StorageModelRegistry_ReturnsNullForUnknownType() {
        Assert.Null(StorageModelRegistry.GetExpectedVersion("NonexistentType"));
    }

    [Fact]
    public void StorageModelRegistry_ReturnsNullForSessionRecords() {
        // Session records should NOT be in the registry
        Assert.Null(StorageModelRegistry.GetExpectedVersion(nameof(KeriaConnectionInfo)));
        Assert.Null(StorageModelRegistry.GetExpectedVersion(nameof(CachedCredentials)));
    }

    [Fact]
    public void Preferences_ImplementsIVersionedStorageModel() {
        var prefs = new Preferences();
        Assert.IsAssignableFrom<IVersionedStorageModel>(prefs);
    }

    [Fact]
    public void Preferences_SchemaVersion_MatchesRegistry() {
        var prefs = new Preferences();
        var expected = StorageModelRegistry.GetExpectedVersion(nameof(Preferences));
        Assert.Equal(expected, prefs.SchemaVersion);
    }

    [Fact]
    public void KeriaConnectConfigs_SchemaVersion_MatchesRegistry() {
        var configs = new KeriaConnectConfigs();
        var expected = StorageModelRegistry.GetExpectedVersion(nameof(KeriaConnectConfigs));
        Assert.Equal(expected, configs.SchemaVersion);
    }

    [Fact]
    public void OnboardState_SchemaVersion_MatchesRegistry() {
        var state = new OnboardState();
        var expected = StorageModelRegistry.GetExpectedVersion(nameof(OnboardState));
        Assert.Equal(expected, state.SchemaVersion);
    }

    [Fact]
    public void WebsiteConfigList_SchemaVersion_MatchesRegistry() {
        var list = new WebsiteConfigList();
        var expected = StorageModelRegistry.GetExpectedVersion(nameof(WebsiteConfigList));
        Assert.Equal(expected, list.SchemaVersion);
    }

    [Fact]
    public void SessionRecords_DoNotImplementIVersionedStorageModel() {
        Assert.IsNotAssignableFrom<IVersionedStorageModel>(new KeriaConnectionInfo { KeriaConnectionDigest = "" });
        Assert.IsNotAssignableFrom<IVersionedStorageModel>(new CachedCredentials());
    }
}
