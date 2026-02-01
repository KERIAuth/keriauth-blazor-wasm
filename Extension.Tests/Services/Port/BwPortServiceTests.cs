namespace Extension.Tests.Services.Port;

using Extension.Models;
using Extension.Models.Storage;
using Extension.Services;
using Extension.Services.Port;
using FluentResults;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Moq;
using WebExtensions.Net;
using Xunit;

/// <summary>
/// Unit tests for BwPortService, focusing on orphaned request cleanup logic.
/// </summary>
public class BwPortServiceTests {
    private readonly Mock<ILogger<BwPortService>> _mockLogger;
    private readonly Mock<IJSRuntime> _mockJsRuntime;
    private readonly Mock<IWebExtensionsApi> _mockWebExtensionsApi;
    private readonly Mock<IPendingBwAppRequestService> _mockPendingRequestService;
    private readonly BwPortService _sut;

    public BwPortServiceTests() {
        _mockLogger = new Mock<ILogger<BwPortService>>();
        _mockJsRuntime = new Mock<IJSRuntime>();
        _mockWebExtensionsApi = new Mock<IWebExtensionsApi>();
        _mockPendingRequestService = new Mock<IPendingBwAppRequestService>();

        _sut = new BwPortService(
            _mockLogger.Object,
            _mockJsRuntime.Object,
            _mockWebExtensionsApi.Object,
            _mockPendingRequestService.Object
        );
    }

    #region CleanupOrphanedRequestsAsync Tests

    [Fact]
    public async Task CleanupOrphanedRequestsAsync_WhenNoPendingRequests_DoesNothing() {
        // Arrange
        var portSession = new PortSession { PortSessionId = Guid.NewGuid() };
        _mockPendingRequestService
            .Setup(x => x.GetRequestsAsync())
            .ReturnsAsync(Result.Ok(PendingBwAppRequests.Empty));

        // Act
        await _sut.CleanupOrphanedRequestsAsync(portSession);

        // Assert
        _mockPendingRequestService.Verify(
            x => x.RemoveRequestAsync(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task CleanupOrphanedRequestsAsync_WhenAppHasNoTabId_TreatsAllRequestsAsOrphaned() {
        // Arrange - App PortSession has no TabId (popup that didn't attach)
        var portSession = new PortSession {
            PortSessionId = Guid.NewGuid(),
            TabId = null  // No TabId
        };

        var pendingRequest = new PendingBwAppRequest {
            RequestId = "test-request-123",
            Type = "BwApp.TestRequest",
            CreatedAtUtc = DateTime.UtcNow,
            TabId = 42,
            PortId = "cs-port-id",
            PortSessionId = Guid.NewGuid().ToString(),
            RpcRequestId = "rpc-123"
        };

        _mockPendingRequestService
            .Setup(x => x.GetRequestsAsync())
            .ReturnsAsync(Result.Ok(new PendingBwAppRequests { Requests = [pendingRequest] }));
        _mockPendingRequestService
            .Setup(x => x.RemoveRequestAsync("test-request-123"))
            .ReturnsAsync(Result.Ok());

        // Act
        await _sut.CleanupOrphanedRequestsAsync(portSession);

        // Assert - Request should be removed as orphaned
        _mockPendingRequestService.Verify(
            x => x.RemoveRequestAsync("test-request-123"),
            Times.Once);
    }

    [Fact]
    public async Task CleanupOrphanedRequestsAsync_WhenTabIdsMatch_TreatsRequestAsOrphaned() {
        // Arrange - App PortSession has matching TabId
        var portSession = new PortSession {
            PortSessionId = Guid.NewGuid(),
            TabId = 42  // Matches request's TabId
        };

        var pendingRequest = new PendingBwAppRequest {
            RequestId = "test-request-456",
            Type = "BwApp.TestRequest",
            CreatedAtUtc = DateTime.UtcNow,
            TabId = 42,  // Matches portSession.TabId
            PortId = "cs-port-id",
            PortSessionId = Guid.NewGuid().ToString(),
            RpcRequestId = "rpc-456"
        };

        _mockPendingRequestService
            .Setup(x => x.GetRequestsAsync())
            .ReturnsAsync(Result.Ok(new PendingBwAppRequests { Requests = [pendingRequest] }));
        _mockPendingRequestService
            .Setup(x => x.RemoveRequestAsync("test-request-456"))
            .ReturnsAsync(Result.Ok());

        // Act
        await _sut.CleanupOrphanedRequestsAsync(portSession);

        // Assert - Request should be removed as orphaned
        _mockPendingRequestService.Verify(
            x => x.RemoveRequestAsync("test-request-456"),
            Times.Once);
    }

    [Fact]
    public async Task CleanupOrphanedRequestsAsync_WhenTabIdsDontMatch_DoesNotRemoveRequest() {
        // Arrange - App PortSession has different TabId
        var portSession = new PortSession {
            PortSessionId = Guid.NewGuid(),
            TabId = 99  // Different from request's TabId
        };

        var pendingRequest = new PendingBwAppRequest {
            RequestId = "test-request-789",
            Type = "BwApp.TestRequest",
            CreatedAtUtc = DateTime.UtcNow,
            TabId = 42,  // Different from portSession.TabId
            PortId = "cs-port-id",
            PortSessionId = Guid.NewGuid().ToString(),
            RpcRequestId = "rpc-789"
        };

        _mockPendingRequestService
            .Setup(x => x.GetRequestsAsync())
            .ReturnsAsync(Result.Ok(new PendingBwAppRequests { Requests = [pendingRequest] }));

        // Act
        await _sut.CleanupOrphanedRequestsAsync(portSession);

        // Assert - Request should NOT be removed (different tab)
        _mockPendingRequestService.Verify(
            x => x.RemoveRequestAsync(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task CleanupOrphanedRequestsAsync_WhenGetRequestsFails_LogsWarningAndReturns() {
        // Arrange
        var portSession = new PortSession { PortSessionId = Guid.NewGuid() };
        _mockPendingRequestService
            .Setup(x => x.GetRequestsAsync())
            .ReturnsAsync(Result.Fail<PendingBwAppRequests>("Storage error"));

        // Act
        await _sut.CleanupOrphanedRequestsAsync(portSession);

        // Assert - Should not attempt to remove anything
        _mockPendingRequestService.Verify(
            x => x.RemoveRequestAsync(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task CleanupOrphanedRequestsAsync_WhenRequestHasNoPortInfo_StillRemovesFromStorage() {
        // Arrange - Request without port info (can't send RPC response, but should still cleanup)
        var portSession = new PortSession {
            PortSessionId = Guid.NewGuid(),
            TabId = null
        };

        var pendingRequest = new PendingBwAppRequest {
            RequestId = "test-request-no-port",
            Type = "BwApp.TestRequest",
            CreatedAtUtc = DateTime.UtcNow,
            TabId = 42,
            PortId = null,  // No port info
            PortSessionId = null,
            RpcRequestId = null
        };

        _mockPendingRequestService
            .Setup(x => x.GetRequestsAsync())
            .ReturnsAsync(Result.Ok(new PendingBwAppRequests { Requests = [pendingRequest] }));
        _mockPendingRequestService
            .Setup(x => x.RemoveRequestAsync("test-request-no-port"))
            .ReturnsAsync(Result.Ok());

        // Act
        await _sut.CleanupOrphanedRequestsAsync(portSession);

        // Assert - Request should still be removed from storage even without port info
        _mockPendingRequestService.Verify(
            x => x.RemoveRequestAsync("test-request-no-port"),
            Times.Once);
    }

    #endregion

    #region CleanupAllPendingRequestsAsync Tests

    [Fact]
    public async Task CleanupAllPendingRequestsAsync_WhenNoPendingRequests_DoesNothing() {
        // Arrange
        _mockPendingRequestService
            .Setup(x => x.GetRequestsAsync())
            .ReturnsAsync(Result.Ok(PendingBwAppRequests.Empty));

        // Act
        await _sut.CleanupAllPendingRequestsAsync("Test error message");

        // Assert
        _mockPendingRequestService.Verify(
            x => x.RemoveRequestAsync(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task CleanupAllPendingRequestsAsync_CleansUpAllRequests() {
        // Arrange
        var request1 = new PendingBwAppRequest {
            RequestId = "request-1",
            Type = "BwApp.TestRequest",
            CreatedAtUtc = DateTime.UtcNow,
            TabId = 42,
            PortId = "cs-port-1",
            PortSessionId = Guid.NewGuid().ToString(),
            RpcRequestId = "rpc-1"
        };
        var request2 = new PendingBwAppRequest {
            RequestId = "request-2",
            Type = "BwApp.TestRequest",
            CreatedAtUtc = DateTime.UtcNow,
            TabId = 43,
            PortId = "cs-port-2",
            PortSessionId = Guid.NewGuid().ToString(),
            RpcRequestId = "rpc-2"
        };

        _mockPendingRequestService
            .Setup(x => x.GetRequestsAsync())
            .ReturnsAsync(Result.Ok(new PendingBwAppRequests { Requests = [request1, request2] }));
        _mockPendingRequestService
            .Setup(x => x.RemoveRequestAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Ok());

        // Act
        await _sut.CleanupAllPendingRequestsAsync("KERI Auth locked due to inactivity");

        // Assert - Both requests should be removed
        _mockPendingRequestService.Verify(
            x => x.RemoveRequestAsync("request-1"),
            Times.Once);
        _mockPendingRequestService.Verify(
            x => x.RemoveRequestAsync("request-2"),
            Times.Once);
    }

    [Fact]
    public async Task CleanupAllPendingRequestsAsync_WhenGetRequestsFails_LogsWarningAndReturns() {
        // Arrange
        _mockPendingRequestService
            .Setup(x => x.GetRequestsAsync())
            .ReturnsAsync(Result.Fail<PendingBwAppRequests>("Storage error"));

        // Act
        await _sut.CleanupAllPendingRequestsAsync("Test error");

        // Assert
        _mockPendingRequestService.Verify(
            x => x.RemoveRequestAsync(It.IsAny<string>()),
            Times.Never);
    }

    #endregion
}
