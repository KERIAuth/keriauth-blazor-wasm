using Extension.Services;
using Extension.Services.JsBindings;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Moq;
using Xunit;

namespace Extension.Tests.Services;

public class NetworkConnectivityServiceTests : IAsyncDisposable {
    private readonly Mock<IJsModuleLoader> _mockModuleLoader = new();
    private readonly Mock<IJSObjectReference> _mockModule = new();
    private readonly NetworkConnectivityService _sut;
    private readonly List<bool> _stateChanges = [];

    public NetworkConnectivityServiceTests() {
        var logger = new Mock<ILogger<NetworkConnectivityService>>();

        _mockModuleLoader
            .Setup(x => x.GetModule("networkConnectivityListener"))
            .Returns(_mockModule.Object);

        _sut = new NetworkConnectivityService(_mockModuleLoader.Object, logger.Object);
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
