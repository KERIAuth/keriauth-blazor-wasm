namespace Extension.Tests.Services;

using System.Text.Json;
using Extension.Helper;
using Extension.Models;
using Extension.Models.Storage;
using Extension.Services;
using Extension.Services.Storage;
using FluentResults;
using Microsoft.Extensions.Logging;
using Moq;
using WebExtensions.Net;
using Xunit;

/// <summary>
/// Tests for AppCache initial fetch and sub-cache population.
/// Phase group 2, Phase 1: verifies that the three new sub-cache properties
/// (MyCachedCredentials, MyPollingState, MyWebsiteConfigList) are populated
/// from the bulk read during Initialize().
/// </summary>
public class AppCacheTests : IDisposable {
    private readonly Mock<IStorageService> _mockStorageService;
    private readonly Mock<IStorageGateway> _mockStorageGateway;
    private readonly Mock<IWebExtensionsApi> _mockWebExtensionsApi;
    private readonly AppCache _sut;

    public AppCacheTests() {
        _mockStorageService = new Mock<IStorageService>();
        _mockStorageGateway = new Mock<IStorageGateway>();
        _mockWebExtensionsApi = new Mock<IWebExtensionsApi>();
        var logger = new Mock<ILogger<AppCache>>();

        // StorageObserver<T> constructors call Subscribe<T> — return a disposable stub.
        _mockStorageService
            .Setup(x => x.Subscribe(It.IsAny<IObserver<It.IsAnyType>>(), It.IsAny<StorageArea>()))
            .Returns(Mock.Of<IDisposable>());

        // WaitForBwReadyAsync polls GetItem<BwReadyState> — return ready immediately.
        _mockStorageService
            .Setup(x => x.GetItem<BwReadyState>(StorageArea.Session))
            .ReturnsAsync(Result.Ok<BwReadyState?>(new BwReadyState { IsInitialized = true, InitializedAtUtc = DateTime.UtcNow }));

        _sut = new AppCache(_mockStorageService.Object, _mockStorageGateway.Object, logger.Object, _mockWebExtensionsApi.Object);
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
    public async Task Initialize_PopulatesMyWebsiteConfigList_FromBulkRead() {
        var wcl = new WebsiteConfigList {
            WebsiteList = [
                new WebsiteConfig(new Uri("https://example.com"), [], null, null, false, false, false)
            ]
        };

        SetupBulkReads(
            local: BuildReadResult(
                (nameof(WebsiteConfigList), wcl)
            )
        );

        await _sut.EnsureInitializedAsync();

        Assert.Single(_sut.MyWebsiteConfigList.WebsiteList);
        Assert.Equal(new Uri("https://example.com"), _sut.MyWebsiteConfigList.WebsiteList[0].Origin);
    }

    [Fact]
    public async Task Initialize_DefaultValues_WhenRecordsAbsent() {
        SetupBulkReads(); // empty results

        await _sut.EnsureInitializedAsync();

        Assert.Empty(_sut.MyCachedCredentials);
        Assert.Null(_sut.MyPollingState.IdentifiersLastFetchedUtc);
        Assert.Empty(_sut.MyWebsiteConfigList.WebsiteList);
    }
}
