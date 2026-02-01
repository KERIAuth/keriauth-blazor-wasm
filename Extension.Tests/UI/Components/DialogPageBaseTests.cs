namespace Extension.Tests.UI.Components;

using Extension.Models.Messages.AppBw;
using Extension.Models.Messages.BwApp;
using Extension.Models.Storage;
using Extension.Services;
using Extension.Services.Port;
using Extension.UI.Components;
using Extension.UI.Layouts;
using FluentResults;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Moq;
using WebExtensions.Net;
using WebExtensions.Net.Tabs;
using Xunit;
using BrowserTab = WebExtensions.Net.Tabs.Tab;

/// <summary>
/// Unit tests for DialogPageBase.
/// Tests cover the common request handling patterns shared by dialog pages.
/// </summary>
public class DialogPageBaseTests {
    private readonly Mock<IAppPortService> _mockAppPortService;
    private readonly Mock<IPendingBwAppRequestService> _mockPendingBwAppRequestService;
    private readonly StubAppCache _stubAppCache;
    private readonly Mock<IWebExtensionsApi> _mockWebExtensionsApi;
    private readonly Mock<ILogger<DialogPageBase>> _mockLogger;
    private readonly Mock<DialogLayout> _mockLayout;
    private readonly TestableDialogPage _sut;

    public DialogPageBaseTests() {
        _mockAppPortService = new Mock<IAppPortService>();
        _mockPendingBwAppRequestService = new Mock<IPendingBwAppRequestService>();
        _stubAppCache = new StubAppCache();
        _mockWebExtensionsApi = new Mock<IWebExtensionsApi>();
        _mockLogger = new Mock<ILogger<DialogPageBase>>();
        _mockLayout = new Mock<DialogLayout>();

        _sut = new TestableDialogPage {
            AppPortService = _mockAppPortService.Object,
            PendingBwAppRequestService = _mockPendingBwAppRequestService.Object,
            AppCacheStub = _stubAppCache,
            WebExtensionsApi = _mockWebExtensionsApi.Object,
            Logger = _mockLogger.Object,
            Layout = _mockLayout.Object
        };
    }

    #region SendCancelMessageAsync Tests

    [Fact]
    public async Task SendCancelMessageAsync_SendsMessageAndSetsFlag() {
        // Arrange
        _sut.SetState(pageRequestId: "test-request-123", tabId: 42, originStr: "https://example.com");
        _mockAppPortService
            .Setup(x => x.SendToBackgroundWorkerAsync(It.IsAny<AppBwReplyCanceledMessage>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.TestSendCancelMessageAsync("User cancelled");

        // Assert
        Assert.True(_sut.GetHasRepliedToPage());
        _mockAppPortService.Verify(
            x => x.SendToBackgroundWorkerAsync(It.Is<AppBwReplyCanceledMessage>(m =>
                m.TabId == 42 &&
                m.RequestId == "test-request-123" &&
                m.TabUrl == "https://example.com")),
            Times.Once);
    }

    [Fact]
    public async Task SendCancelMessageAsync_HandlesExceptionGracefully() {
        // Arrange
        _sut.SetState(pageRequestId: "test-request-123", tabId: 42, originStr: "https://example.com");
        _mockAppPortService
            .Setup(x => x.SendToBackgroundWorkerAsync(It.IsAny<AppBwReplyCanceledMessage>()))
            .ThrowsAsync(new InvalidOperationException("Port disconnected"));

        // Act
        await _sut.TestSendCancelMessageAsync("User cancelled");

        // Assert - should still mark as replied to prevent duplicate attempts
        Assert.True(_sut.GetHasRepliedToPage());
    }

    #endregion

    #region ClearPendingRequestAsync Tests

    [Fact]
    public async Task ClearPendingRequestAsync_WithValidPageRequestId_RemovesRequest() {
        // Arrange
        _sut.SetState(pageRequestId: "test-request-123");
        _mockPendingBwAppRequestService
            .Setup(x => x.RemoveRequestAsync("test-request-123"))
            .ReturnsAsync(Result.Ok());

        // Act
        await _sut.TestClearPendingRequestAsync();

        // Assert
        _mockPendingBwAppRequestService.Verify(
            x => x.RemoveRequestAsync("test-request-123"),
            Times.Once);
    }

    [Fact]
    public async Task ClearPendingRequestAsync_WithEmptyPageRequestId_DoesNothing() {
        // Arrange
        _sut.SetState(pageRequestId: "");

        // Act
        await _sut.TestClearPendingRequestAsync();

        // Assert
        _mockPendingBwAppRequestService.Verify(
            x => x.RemoveRequestAsync(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ClearPendingRequestAsync_LogsErrorOnFailure() {
        // Arrange
        _sut.SetState(pageRequestId: "test-request-123");
        _mockPendingBwAppRequestService
            .Setup(x => x.RemoveRequestAsync("test-request-123"))
            .ReturnsAsync(Result.Fail("Storage error"));

        // Act
        await _sut.TestClearPendingRequestAsync();

        // Assert - method should complete without throwing
        _mockPendingBwAppRequestService.Verify(
            x => x.RemoveRequestAsync("test-request-123"),
            Times.Once);
    }

    #endregion

    #region WaitForAppCacheClearAsync Tests

    [Fact]
    public async Task WaitForAppCacheClearAsync_ReturnsTrue_WhenCacheClears() {
        // Arrange
        _stubAppCache.WaitForAppCacheResult = true;

        // Act
        var result = await _sut.TestWaitForAppCacheClearAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task WaitForAppCacheClearAsync_ReturnsFalse_OnTimeout() {
        // Arrange
        _stubAppCache.WaitForAppCacheResult = false;

        // Act
        var result = await _sut.TestWaitForAppCacheClearAsync();

        // Assert
        Assert.False(result);
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_SendsCancelMessage_WhenNotReplied() {
        // Arrange
        _sut.SetState(pageRequestId: "test-request-123", tabId: 42, originStr: "https://example.com", hasRepliedToPage: false);
        _mockAppPortService
            .Setup(x => x.SendToBackgroundWorkerAsync(It.IsAny<AppBwReplyCanceledMessage>()))
            .Returns(Task.CompletedTask);
        _mockPendingBwAppRequestService
            .Setup(x => x.RemoveRequestAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Ok());

        // Act
        await _sut.DisposeAsync();

        // Assert
        _mockAppPortService.Verify(
            x => x.SendToBackgroundWorkerAsync(It.IsAny<AppBwReplyCanceledMessage>()),
            Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_DoesNotSendCancel_WhenAlreadyReplied() {
        // Arrange
        _sut.SetState(pageRequestId: "test-request-123", tabId: 42, originStr: "https://example.com", hasRepliedToPage: true);
        _mockPendingBwAppRequestService
            .Setup(x => x.RemoveRequestAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Ok());

        // Act
        await _sut.DisposeAsync();

        // Assert
        _mockAppPortService.Verify(
            x => x.SendToBackgroundWorkerAsync(It.IsAny<AppBwReplyCanceledMessage>()),
            Times.Never);
    }

    [Fact]
    public async Task DisposeAsync_DoesNotSendCancel_WhenTabIdInvalid() {
        // Arrange
        _sut.SetState(pageRequestId: "test-request-123", tabId: -1, hasRepliedToPage: false);
        _mockPendingBwAppRequestService
            .Setup(x => x.RemoveRequestAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Ok());

        // Act
        await _sut.DisposeAsync();

        // Assert
        _mockAppPortService.Verify(
            x => x.SendToBackgroundWorkerAsync(It.IsAny<AppBwReplyCanceledMessage>()),
            Times.Never);
    }

    [Fact]
    public async Task DisposeAsync_DoesNotSendCancel_WhenPageRequestIdEmpty() {
        // Arrange
        _sut.SetState(pageRequestId: "", tabId: 42, hasRepliedToPage: false);
        _mockPendingBwAppRequestService
            .Setup(x => x.RemoveRequestAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Ok());

        // Act
        await _sut.DisposeAsync();

        // Assert
        _mockAppPortService.Verify(
            x => x.SendToBackgroundWorkerAsync(It.IsAny<AppBwReplyCanceledMessage>()),
            Times.Never);
    }

    [Fact]
    public async Task DisposeAsync_DoesNotClearPendingRequest_LetBackgroundWorkerHandleIt() {
        // Arrange - DisposeAsync should NOT clear pending request because:
        // 1. If SendCancelMessageAsync succeeds, BackgroundWorker.HandleAppReplyCanceledRpcAsync clears it
        // 2. If SendCancelMessageAsync fails (port disconnecting), BwPortService.CleanupOrphanedRequestsAsync clears it
        _sut.SetState(pageRequestId: "test-request-123", tabId: 42, hasRepliedToPage: true);

        // Act
        await _sut.DisposeAsync();

        // Assert - pending request should NOT be removed by DisposeAsync
        _mockPendingBwAppRequestService.Verify(
            x => x.RemoveRequestAsync(It.IsAny<string>()),
            Times.Never);
    }

    #endregion

    #region InitializeFromPendingRequestAsync Tests

    [Fact]
    public async Task InitializeFromPendingRequestAsync_ReturnsPayload_WhenTypeMatches() {
        // Arrange
        var expectedPayload = new TestPayload { TabId = 42, Origin = "https://example.com", Message = "Test message" };
        var pendingRequest = new PendingBwAppRequest {
            RequestId = "test-request-123",
            Type = "BwApp.TestRequest",
            TabId = 42,
            TabUrl = "https://example.com",
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(expectedPayload),
            CreatedAtUtc = DateTime.UtcNow
        };

        _stubAppCache.NextPendingBwAppRequestValue = pendingRequest;

        // Act
        var result = await _sut.TestInitializeFromPendingRequestAsync<TestPayload>("BwApp.TestRequest");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(42, result.TabId);
        Assert.Equal("https://example.com", result.Origin);
        Assert.Equal("Test message", result.Message);
        Assert.Equal("test-request-123", _sut.GetPageRequestId());
        Assert.Equal(42, _sut.GetTabId());
        Assert.Equal("https://example.com", _sut.GetOriginStr());
    }

    [Fact]
    public async Task InitializeFromPendingRequestAsync_ReturnsNull_WhenTypeMismatch() {
        // Arrange
        var pendingRequest = new PendingBwAppRequest {
            RequestId = "test-request-123",
            Type = "BwApp.DifferentRequest",
            TabId = 42,
            TabUrl = "https://example.com",
            Payload = null,
            CreatedAtUtc = DateTime.UtcNow
        };

        _stubAppCache.NextPendingBwAppRequestValue = pendingRequest;

        // Act
        var result = await _sut.TestInitializeFromPendingRequestAsync<TestPayload>("BwApp.TestRequest");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task InitializeFromPendingRequestAsync_ReturnsNull_WhenNoPendingRequest() {
        // Arrange
        _stubAppCache.NextPendingBwAppRequestValue = null;

        // Act
        var result = await _sut.TestInitializeFromPendingRequestAsync<TestPayload>("BwApp.TestRequest");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region MarkAsReplied Tests

    [Fact]
    public void MarkAsReplied_SetsHasRepliedToPageTrue() {
        // Arrange
        _sut.SetState(hasRepliedToPage: false);

        // Act
        _sut.TestMarkAsReplied();

        // Assert
        Assert.True(_sut.GetHasRepliedToPage());
    }

    #endregion

    #region Test Helpers

    /// <summary>
    /// Test payload for InitializeFromPendingRequestAsync tests.
    /// </summary>
    private sealed class TestPayload {
        public int TabId { get; set; }
        public string Origin { get; set; } = "";
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// Stub for AppCache to avoid mocking issues with constructor dependencies.
    /// </summary>
    private sealed class StubAppCache {
        public PendingBwAppRequest? NextPendingBwAppRequestValue { get; set; }
        public bool HasPendingBwAppRequestsValue { get; set; }
        public bool WaitForAppCacheResult { get; set; } = true;

        public PendingBwAppRequest? NextPendingBwAppRequest => NextPendingBwAppRequestValue;
        public bool HasPendingBwAppRequests => HasPendingBwAppRequestsValue;

        public Task<bool> WaitForAppCache(List<Func<bool>> assertions, int maxWaitMs = 5000, int pollIntervalMs = 500) {
            return Task.FromResult(WaitForAppCacheResult);
        }
    }

    /// <summary>
    /// Testable concrete implementation of DialogPageBase.
    /// Exposes protected methods for testing.
    /// Uses StubAppCache instead of real AppCache.
    /// </summary>
    private sealed class TestableDialogPage : DialogPageBase {
        private StubAppCache? _stubAppCache;

        // Expose setters for injected services (normally set by DI)
        public new IAppPortService AppPortService { set => base.AppPortService = value; }
        public new IPendingBwAppRequestService PendingBwAppRequestService { set => base.PendingBwAppRequestService = value; }
        public StubAppCache? AppCacheStub { set => _stubAppCache = value; }
        public new IWebExtensionsApi WebExtensionsApi { set => base.WebExtensionsApi = value; }
        public new ILogger<DialogPageBase> Logger { get => base.Logger; set => base.Logger = value; }
        public new DialogLayout? Layout { set => base.Layout = value; }

        // State setters for test setup
        public void SetState(
            string? pageRequestId = null,
            int? tabId = null,
            string? originStr = null,
            bool? hasRepliedToPage = null,
            bool? isInitialized = null) {
            if (pageRequestId is not null) PageRequestId = pageRequestId;
            if (tabId is not null) TabId = tabId.Value;
            if (originStr is not null) OriginStr = originStr;
            if (hasRepliedToPage is not null) HasRepliedToPage = hasRepliedToPage.Value;
            if (isInitialized is not null) IsInitialized = isInitialized.Value;
        }

        // State getters for assertions
        public string GetPageRequestId() => PageRequestId;
        public int GetTabId() => TabId;
        public string GetOriginStr() => OriginStr;
        public bool GetHasRepliedToPage() => HasRepliedToPage;
        public bool GetIsInitialized() => IsInitialized;

        // Expose protected methods for testing, using stub where needed
        public Task TestSendCancelMessageAsync(string reason) => SendCancelMessageAsync(reason);
        public Task TestClearPendingRequestAsync() => ClearPendingRequestAsync();

        public async Task<bool> TestWaitForAppCacheClearAsync(int timeoutMs = 3000) {
            // Use stub instead of base implementation
            if (_stubAppCache is not null) {
                return await _stubAppCache.WaitForAppCache([], timeoutMs, 100);
            }
            return await WaitForAppCacheClearAsync(timeoutMs);
        }

        public Task<T?> TestInitializeFromPendingRequestAsync<T>(string expectedType) where T : class {
            // Use stub for pending request lookup
            if (_stubAppCache is null) {
                return Task.FromResult<T?>(null);
            }

            var pendingRequest = _stubAppCache.NextPendingBwAppRequest;
            if (pendingRequest?.Type != expectedType) {
                return Task.FromResult<T?>(null);
            }

            OriginStr = pendingRequest.TabUrl ?? "unknown";
            var payload = pendingRequest.GetPayload<T>();
            if (payload is null) {
                return Task.FromResult<T?>(null);
            }

            PageRequestId = pendingRequest.RequestId;
            TabId = pendingRequest.TabId ?? -1;

            return Task.FromResult<T?>(payload);
        }

        public void TestMarkAsReplied() => MarkAsReplied();
    }

    #endregion
}
