using Extension.Services;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Moq;
using Xunit;

namespace Extension.Tests.Services;

public class NetworkConnectivityServiceTests : IAsyncDisposable {
    private readonly Mock<IJSRuntime> _mockJsRuntime = new();
    private readonly Mock<IJSObjectReference> _mockModule = new();
    private readonly NetworkConnectivityService _sut;
    private readonly List<bool> _stateChanges = [];

    public NetworkConnectivityServiceTests() {
        var logger = new Mock<ILogger<NetworkConnectivityService>>();

        _mockJsRuntime
            .Setup(x => x.InvokeAsync<IJSObjectReference>(
                "import",
                It.IsAny<object[]>()))
            .ReturnsAsync(_mockModule.Object);

        _sut = new NetworkConnectivityService(_mockJsRuntime.Object, logger.Object);
        _sut.OnlineStateChanged += (isOnline) => _stateChanges.Add(isOnline);
    }

    public async ValueTask DisposeAsync() {
        await _sut.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void IsOnline_DefaultsToTrue() {
        Assert.True(_sut.IsOnline);
    }

    [Fact]
    public async Task StartListeningAsync_LoadsModule() {
        await _sut.StartListeningAsync();

        _mockJsRuntime.Verify(x => x.InvokeAsync<IJSObjectReference>(
            "import",
            It.Is<object[]>(args => args.Length == 1 && ((string)args[0]).Contains("networkConnectivityListener"))),
            Times.Once);
    }

    [Fact]
    public async Task OnNetworkStateChanged_True_SetsIsOnlineAndFiresEvent() {
        // Simulate going offline then back online
        await _sut.OnNetworkStateChanged(false);
        Assert.False(_sut.IsOnline);
        Assert.Single(_stateChanges);
        Assert.False(_stateChanges[0]);

        await _sut.OnNetworkStateChanged(true);
        Assert.True(_sut.IsOnline);
        Assert.Equal(2, _stateChanges.Count);
        Assert.True(_stateChanges[1]);
    }

    [Fact]
    public async Task OnNetworkStateChanged_SameState_DoesNotFireEvent() {
        // Default is true, so setting true again should not fire
        await _sut.OnNetworkStateChanged(true);
        Assert.Empty(_stateChanges);
    }

    [Fact]
    public async Task OnNetworkStateChanged_AfterDispose_DoesNotFireEvent() {
        await _sut.DisposeAsync();

        await _sut.OnNetworkStateChanged(false);
        Assert.Empty(_stateChanges);
        Assert.True(_sut.IsOnline); // unchanged
    }
}
