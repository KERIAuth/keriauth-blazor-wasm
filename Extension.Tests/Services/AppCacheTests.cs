namespace Extension.Tests.Services;

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Extension.Helper;
using Extension.Models;
using Extension.Models.Storage;
using Extension.Services;
using Extension.Services.Storage;
using FluentResults;
using Microsoft.Extensions.Logging;
using Moq;
using WebExtensions.Net;
using WebExtensions.Net.Mock;
using Xunit;

/// <summary>
/// Tests for AppCache initial fetch and sub-cache population.
/// Phase group 2, Phase 1: verifies that the three new sub-cache properties
/// (MyCachedCredentials, MyPollingState, MyWebsiteConfigs) are populated
/// from the bulk read during Initialize().
/// </summary>
public class AppCacheTests : IDisposable {
    private readonly Mock<IStorageGateway> _mockStorageGateway;
    private readonly Mock<IWebExtensionsApi> _mockWebExtensionsApi;
    private readonly AppCache _sut;

    public AppCacheTests() {
        _mockStorageGateway = new Mock<IStorageGateway>();
        _mockWebExtensionsApi = new Mock<IWebExtensionsApi>();
        var logger = new Mock<ILogger<AppCache>>();

        // WaitForBwReadyAsync polls GetItem<BwReadyState> — return ready immediately.
        _mockStorageGateway
            .Setup(x => x.GetItem<BwReadyState>(StorageArea.Session))
            .ReturnsAsync(Result.Ok<BwReadyState?>(new BwReadyState { IsInitialized = true, InitializedAtUtc = DateTime.UtcNow }));

        _sut = new AppCache(_mockStorageGateway.Object, logger.Object, _mockWebExtensionsApi.Object);
    }

    public void Dispose() {
        _sut.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Helper to build a StorageReadResult from a dictionary of pre-serialized models.
    /// </summary>
    private static StorageReadResult BuildReadResult(params (string key, object? value)[] entries) {
        var raw = new Dictionary<string, JsonElement?>();
        foreach (var (key, value) in entries) {
            if (value is null) {
                raw[key] = null;
            }
            else {
                var json = JsonSerializer.Serialize(value, JsonOptions.Storage);
                raw[key] = JsonDocument.Parse(json).RootElement.Clone();
            }
        }
        return new StorageReadResult(raw, [], null);
    }

    private void SetupBulkReads(StorageReadResult? local = null, StorageReadResult? session = null) {
        _mockStorageGateway
            .Setup(x => x.GetItems(StorageArea.Local, It.IsAny<Type[]>()))
            .ReturnsAsync(Result.Ok(local ?? BuildReadResult()));
        _mockStorageGateway
            .Setup(x => x.GetItems(StorageArea.Session, It.IsAny<Type[]>()))
            .ReturnsAsync(Result.Ok(session ?? BuildReadResult()));
    }

    [Fact]
    public async Task Initialize_PopulatesMyCachedCredentials_FromBulkRead() {
        var creds = new CachedCredentials {
            Credentials = new Dictionary<string, string> {
                ["SAID1"] = """{"v":"ACDC10JSON000197_","d":"SAID1","i":"issuer","ri":"registry","s":"schema","a":{"d":"attr-said","dt":"2024-01-01T00:00:00Z","i":"issuee","LEI":"ABC123"}}""",
                ["SAID2"] = """{"v":"ACDC10JSON000197_","d":"SAID2","i":"issuer","ri":"registry","s":"schema","a":{"d":"attr-said","dt":"2024-01-01T00:00:00Z","i":"issuee","LEI":"DEF456"}}"""
            }
        };

        SetupBulkReads(
            session: BuildReadResult(
                (nameof(CachedCredentials), creds)
            )
        );

        await _sut.EnsureInitializedAsync();

        Assert.Equal(2, _sut.MyCachedCredentials.Count);
    }

    [Fact]
    public async Task Initialize_PopulatesMyPollingState_FromBulkRead() {
        var ps = new PollingState {
            IdentifiersLastFetchedUtc = new DateTime(2026, 4, 5, 10, 0, 0, DateTimeKind.Utc),
            CredentialsLastFetchedUtc = new DateTime(2026, 4, 5, 10, 1, 0, DateTimeKind.Utc),
            NotificationsLastFetchedUtc = new DateTime(2026, 4, 5, 10, 2, 0, DateTimeKind.Utc),
        };

        SetupBulkReads(
            session: BuildReadResult(
                (nameof(PollingState), ps)
            )
        );

        await _sut.EnsureInitializedAsync();

        Assert.Equal(ps.IdentifiersLastFetchedUtc, _sut.MyPollingState.IdentifiersLastFetchedUtc);
        Assert.Equal(ps.CredentialsLastFetchedUtc, _sut.MyPollingState.CredentialsLastFetchedUtc);
        Assert.Equal(ps.NotificationsLastFetchedUtc, _sut.MyPollingState.NotificationsLastFetchedUtc);
    }

    [Fact]
    public async Task Initialize_DefaultValues_WhenRecordsAbsent() {
        SetupBulkReads(); // empty results

        await _sut.EnsureInitializedAsync();

        Assert.Empty(_sut.MyCachedCredentials);
        Assert.Null(_sut.MyPollingState.IdentifiersLastFetchedUtc);
        Assert.Empty(_sut.MyWebsiteConfigs);
    }

    [Fact]
    public async Task Initialize_PopulatesMyNetworkState_FromBulkRead() {
        var ns = new NetworkState { IsOnline = false, IsKeriaReachable = false };

        SetupBulkReads(
            session: BuildReadResult(
                (nameof(NetworkState), ns)
            )
        );

        await _sut.EnsureInitializedAsync();

        Assert.False(_sut.IsNetworkOnline);
        Assert.False(_sut.IsKeriaReachable);
    }

    [Fact]
    public async Task Initialize_NetworkStateDefaults_WhenAbsent() {
        SetupBulkReads(); // empty results

        await _sut.EnsureInitializedAsync();

        Assert.True(_sut.IsNetworkOnline);
        Assert.True(_sut.IsKeriaReachable);
    }
}

/// <summary>
/// Tests for AppCache's BatchObserver — verifies that storage change events
/// dispatched through StorageGateway update the correct My* properties and
/// fire Changed exactly once per batch.
/// Uses a real StorageGateway with reflection into _globalCallback to simulate
/// Chrome storage.onChanged events (same pattern as StorageGatewayNotificationTests).
/// </summary>
[Collection("StorageService Sequential Tests")]
public class AppCacheBatchTests : IAsyncLifetime, IDisposable {
    private readonly StorageGateway _realGateway;
    private readonly AppCache _sut;
    private int _changedCount;

    private sealed record StorageChange<T>(
        [property: JsonPropertyName("newValue")] T? NewValue,
        [property: JsonPropertyName("oldValue")] T? OldValue);

    private static readonly JsonSerializerOptions JsonOpts = new() {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
        PropertyNamingPolicy = null,
    };

    public AppCacheBatchTests() {
        var mockJsRuntime = new MockJsRuntimeAdapter();
        _realGateway = new StorageGateway(mockJsRuntime, new Mock<ILogger<StorageGateway>>().Object);
        var mockWebExtensionsApi = new Mock<IWebExtensionsApi>();
        var logger = new Mock<ILogger<AppCache>>();

        _sut = new AppCache(_realGateway, logger.Object, mockWebExtensionsApi.Object);
    }

    public async Task InitializeAsync() {
        // Call Initialize() directly (skips WaitForBwReadyAsync which needs a real BW).
        await _sut.Initialize();
        _sut.Changed += () => _changedCount++;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() {
        _sut.Dispose();
        _realGateway.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void BatchChange_CachedCredentials_UpdatesMyCachedCredentials() {
        var creds = new CachedCredentials {
            Credentials = new Dictionary<string, string> {
                ["SAID1"] = """{"v":"ACDC10JSON000197_","d":"SAID1","i":"issuer","ri":"registry","s":"schema","a":{"d":"attr-said","dt":"2024-01-01T00:00:00Z","i":"issuee","LEI":"ABC123"}}"""
            }
        };

        TriggerStorageChange("session", nameof(CachedCredentials), creds, default(CachedCredentials));

        Assert.Single(_sut.MyCachedCredentials);
        Assert.Equal(1, _changedCount);
    }

    [Fact]
    public void BatchChange_PollingState_UpdatesMyPollingState() {
        var ps = new PollingState {
            IdentifiersLastFetchedUtc = new DateTime(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc),
        };

        TriggerStorageChange("session", nameof(PollingState), ps, default(PollingState));

        Assert.Equal(ps.IdentifiersLastFetchedUtc, _sut.MyPollingState.IdentifiersLastFetchedUtc);
        Assert.Equal(1, _changedCount);
    }

    [Fact]
    public void BatchChange_MultipleNewRecordsInOneBatch_FiresChangedOnce() {
        var creds = new CachedCredentials {
            Credentials = new Dictionary<string, string> {
                ["SAID1"] = """{"v":"ACDC10JSON000197_","d":"SAID1","i":"issuer","ri":"registry","s":"schema","a":{"d":"attr-said","dt":"2024-01-01T00:00:00Z","i":"issuee","LEI":"X"}}"""
            }
        };
        var ps = new PollingState {
            CredentialsLastFetchedUtc = new DateTime(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc),
        };

        // Fire both in one Chrome batch (same area = session)
        TriggerMultiKeyStorageChange("session", new Dictionary<string, object?> {
            [nameof(CachedCredentials)] = creds,
            [nameof(PollingState)] = ps,
        });

        Assert.Single(_sut.MyCachedCredentials);
        Assert.Equal(ps.CredentialsLastFetchedUtc, _sut.MyPollingState.CredentialsLastFetchedUtc);
        Assert.Equal(1, _changedCount);
    }

    [Fact]
    public void BatchChange_DeletionOfCachedCredentials_ClearsMyCachedCredentials() {
        // First set some credentials
        var creds = new CachedCredentials {
            Credentials = new Dictionary<string, string> {
                ["SAID1"] = """{"v":"ACDC10JSON000197_","d":"SAID1","i":"issuer","ri":"registry","s":"schema","a":{"d":"attr-said","dt":"2024-01-01T00:00:00Z","i":"issuee","LEI":"X"}}"""
            }
        };
        TriggerStorageChange("session", nameof(CachedCredentials), creds, default(CachedCredentials));
        Assert.Single(_sut.MyCachedCredentials);
        _changedCount = 0;

        // Now delete
        TriggerDeletionEvent("session", nameof(CachedCredentials), creds);

        Assert.Empty(_sut.MyCachedCredentials);
        Assert.Equal(1, _changedCount);
    }

    [Fact]
    public void BatchChange_Preferences_UpdatesMyPreferences() {
        var prefs = AppConfig.DefaultPreferences with { IsDarkTheme = true };

        TriggerStorageChange("local", nameof(Preferences), prefs, default(Preferences));

        Assert.True(_sut.MyPreferences.IsDarkTheme);
        Assert.Equal(1, _changedCount);
    }

    [Fact]
    public void BatchChange_SessionStateModel_UpdatesMySessionState() {
        var expiry = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        var ssm = new SessionStateModel { SessionExpirationUtc = expiry };

        TriggerStorageChange("session", nameof(SessionStateModel), ssm, default(SessionStateModel));

        Assert.Equal(expiry, _sut.MySessionState.SessionExpirationUtc);
        Assert.Equal(1, _changedCount);
    }

    [Fact]
    public void BatchChange_CachedIdentifiers_UpdatesAndCallsValidate() {
        var ids = new CachedIdentifiers { IdentifiersList = [] };

        TriggerStorageChange("session", nameof(CachedIdentifiers), ids, default(CachedIdentifiers));

        Assert.Empty(_sut.MyCachedIdentifiers.IdentifiersList);
        Assert.Equal(1, _changedCount);
    }

    [Fact]
    public void BatchChange_MultipleLocalRecords_FiresChangedOnce() {
        var prefs = AppConfig.DefaultPreferences with { IsDarkTheme = true };
        var onboard = new OnboardState { ShowedGettingStarted = true };

        TriggerMultiKeyStorageChange("local", new Dictionary<string, object?> {
            [nameof(Preferences)] = prefs,
            [nameof(OnboardState)] = onboard,
        });

        Assert.True(_sut.MyPreferences.IsDarkTheme);
        Assert.True(_sut.MyOnboardState.ShowedGettingStarted);
        Assert.Equal(1, _changedCount);
    }

    [Fact]
    public void BatchChange_DeletionOfSessionState_FallsBackToDefault() {
        // First set a value
        var expiry = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        var ssm = new SessionStateModel { SessionExpirationUtc = expiry };
        TriggerStorageChange("session", nameof(SessionStateModel), ssm, default(SessionStateModel));
        _changedCount = 0;

        // Now delete
        TriggerDeletionEvent("session", nameof(SessionStateModel), ssm);

        Assert.Equal(DateTime.MinValue, _sut.MySessionState.SessionExpirationUtc);
        Assert.Equal(1, _changedCount);
    }

    [Fact]
    public void BatchChange_NetworkState_UpdatesMyNetworkState() {
        var ns = new NetworkState { IsOnline = false, IsKeriaReachable = false };

        TriggerStorageChange("session", nameof(NetworkState), ns, default(NetworkState));

        Assert.False(_sut.IsNetworkOnline);
        Assert.False(_sut.IsKeriaReachable);
        Assert.Equal(1, _changedCount);
    }

    [Fact]
    public void BatchChange_NetworkState_DefaultsWhenNull() {
        // First set offline
        TriggerStorageChange("session", nameof(NetworkState),
            new NetworkState { IsOnline = false }, default(NetworkState));
        _changedCount = 0;

        // Delete (simulates session clear)
        TriggerDeletionEvent("session", nameof(NetworkState),
            new NetworkState { IsOnline = false });

        // Should fall back to defaults (online=true, reachable=true)
        Assert.True(_sut.IsNetworkOnline);
        Assert.True(_sut.IsKeriaReachable);
        Assert.Equal(1, _changedCount);
    }

    [Fact]
    public void BatchChange_UnknownKey_DoesNotFireChanged() {
        // A storage key that AppCache doesn't track should not trigger Changed
        TriggerStorageChange("local", "SomeUnknownRecord", new { Foo = "bar" }, default(object));

        Assert.Equal(0, _changedCount);
    }

    // -------- Test helpers (reflection into _globalCallback) --------

    private void TriggerStorageChange<T>(string areaName, string key, T newValue, T? oldValue) {
        var change = new StorageChange<T>(newValue, oldValue);
        var changes = new Dictionary<string, StorageChange<T>> { [key] = change };
        var json = JsonSerializer.Serialize(changes, JsonOpts);
        var element = JsonDocument.Parse(json).RootElement;
        InvokeGlobalCallback(element, areaName);
    }

    private void TriggerMultiKeyStorageChange(string areaName, Dictionary<string, object?> newValuesByKey) {
        var changes = new Dictionary<string, object>();
        foreach (var (key, value) in newValuesByKey) {
            changes[key] = new { newValue = value };
        }
        var json = JsonSerializer.Serialize(changes, JsonOpts);
        var element = JsonDocument.Parse(json).RootElement;
        InvokeGlobalCallback(element, areaName);
    }

    private void TriggerDeletionEvent<T>(string areaName, string key, T oldValue) {
        var changes = new Dictionary<string, object> {
            [key] = new { oldValue = oldValue }
        };
        var json = JsonSerializer.Serialize(changes, JsonOpts);
        var element = JsonDocument.Parse(json).RootElement;
        InvokeGlobalCallback(element, areaName);
    }

    private void InvokeGlobalCallback(object changes, string areaName) {
        var field = typeof(StorageGateway).GetField(
            "_globalCallback", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        var callback = field!.GetValue(_realGateway) as Action<object, string>;
        Assert.NotNull(callback);
        callback!.Invoke(changes, areaName);
    }
}
