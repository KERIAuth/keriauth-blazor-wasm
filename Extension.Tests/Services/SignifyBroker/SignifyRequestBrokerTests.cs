using Extension.Models;
using Extension.Services.SignifyBroker;
using Extension.Services.SignifyService;
using FluentResults;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Extension.Tests.Services.SignifyBroker;

public class SignifyRequestBrokerTests : IAsyncDisposable {
    private readonly Mock<ISignifyClientService> _mockClient = new();
    private readonly SignifyRequestBroker _broker;

    public SignifyRequestBrokerTests() {
        var mockLogger = new Mock<ILogger<SignifyRequestBroker>>();
        _broker = new SignifyRequestBroker(_mockClient.Object, mockLogger.Object);
    }

    public async ValueTask DisposeAsync() {
        await _broker.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task EnqueueReadAsync_Success_ReturnsResult() {
        var result = await _broker.EnqueueReadAsync(SignifyOperation.GetState,
            _ => Task.FromResult(Result.Ok("hello")));

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public async Task EnqueueCommandAsync_Success_ReturnsResult() {
        var result = await _broker.EnqueueCommandAsync(SignifyOperation.Connect,
            _ => Task.FromResult(Result.Ok(42)));

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task EnqueueCommandAsync_NonGeneric_Success() {
        var result = await _broker.EnqueueCommandAsync(SignifyOperation.Disconnect,
            _ => Task.FromResult(Result.Ok()));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task EnqueueBackgroundAsync_Success_ReturnsResult() {
        var result = await _broker.EnqueueBackgroundAsync(SignifyOperation.ListNotifications,
            _ => Task.FromResult(Result.Ok("bg")));

        Assert.True(result.IsSuccess);
        Assert.Equal("bg", result.Value);
    }

    [Fact]
    public async Task EnqueueReadAsync_Failure_PropagatesErrors() {
        var result = await _broker.EnqueueReadAsync<string>(SignifyOperation.GetState,
            _ => Task.FromResult(Result.Fail<string>("something broke")));

        Assert.True(result.IsFailed);
        Assert.Contains("something broke", result.Errors[0].Message);
    }

    [Fact]
    public async Task Commands_ExecuteBeforeReads() {
        var order = new List<string>();
        var gate = new TaskCompletionSource();

        // Enqueue a blocking command to hold the drain loop
        var blockingTask = _broker.EnqueueCommandAsync(SignifyOperation.Connect,
            async _ => {
                await gate.Task;
                order.Add("blocking");
                return Result.Ok("done");
            });

        // Give the drain loop time to pick up the blocking command
        await Task.Delay(50);

        // Now enqueue a read and a command while the loop is blocked
        var readTask = _broker.EnqueueReadAsync(SignifyOperation.GetState,
            _ => { order.Add("read"); return Task.FromResult(Result.Ok("r")); });

        var cmdTask = _broker.EnqueueCommandAsync(SignifyOperation.Disconnect,
            _ => { order.Add("command"); return Task.FromResult(Result.Ok("c")); });

        // Release the blocking command
        gate.SetResult();

        await Task.WhenAll(blockingTask, readTask, cmdTask);

        // Command should execute before read (both were queued while blocked)
        Assert.Equal("blocking", order[0]);
        Assert.Equal("command", order[1]);
        Assert.Equal("read", order[2]);
    }

    [Fact]
    public async Task ReadsExecuteBeforeBackground() {
        var order = new List<string>();
        var gate = new TaskCompletionSource();

        var blockingTask = _broker.EnqueueCommandAsync(SignifyOperation.Connect,
            async _ => {
                await gate.Task;
                order.Add("blocking");
                return Result.Ok("done");
            });

        await Task.Delay(50);

        var bgTask = _broker.EnqueueBackgroundAsync(SignifyOperation.ListNotifications,
            _ => { order.Add("bg"); return Task.FromResult(Result.Ok("b")); });

        var readTask = _broker.EnqueueReadAsync(SignifyOperation.GetState,
            _ => { order.Add("read"); return Task.FromResult(Result.Ok("r")); });

        gate.SetResult();
        await Task.WhenAll(blockingTask, readTask, bgTask);

        Assert.Equal("blocking", order[0]);
        Assert.Equal("read", order[1]);
        Assert.Equal("bg", order[2]);
    }

    [Fact]
    public async Task PrioritizeInteractive_PreventsBackgroundExecution() {
        var order = new List<string>();
        var gate = new TaskCompletionSource();

        // Start a blocking command
        var blockingTask = _broker.EnqueueCommandAsync(SignifyOperation.Connect,
            async _ => {
                await gate.Task;
                order.Add("blocking");
                return Result.Ok("done");
            });

        await Task.Delay(50);

        // Suspend background
        var suspension = _broker.PrioritizeInteractive();

        // Enqueue background and read while suspended
        var bgTask = _broker.EnqueueBackgroundAsync(SignifyOperation.ListNotifications,
            _ => { order.Add("bg"); return Task.FromResult(Result.Ok("b")); });

        var readTask = _broker.EnqueueReadAsync(SignifyOperation.GetState,
            _ => { order.Add("read"); return Task.FromResult(Result.Ok("r")); });

        // Release the blocking command
        gate.SetResult();

        // Read should complete, background should not (yet)
        await blockingTask;
        await readTask;

        Assert.Equal("blocking", order[0]);
        Assert.Equal("read", order[1]);
        Assert.DoesNotContain("bg", order);

        // Resume background
        suspension.Dispose();
        await bgTask;

        Assert.Contains("bg", order);
    }

    [Fact]
    public void Reachability_StartsTrue() {
        Assert.True(_broker.IsKeriaReachable);
    }

    [Fact]
    public async Task Reachability_SingleFailure_StaysTrue() {
        await _broker.EnqueueReadAsync<string>(SignifyOperation.GetState,
            _ => Task.FromResult(Result.Fail<string>(
                new ConnectionError("keria", "Failed to fetch"))));

        Assert.True(_broker.IsKeriaReachable);
    }

    [Fact]
    public async Task Reachability_ThreeConsecutiveFailures_BecomesFalse() {
        bool? lastReachable = null;
        _broker.KeriaReachabilityChanged += r => lastReachable = r;

        for (int i = 0; i < 3; i++) {
            await _broker.EnqueueReadAsync<string>(SignifyOperation.GetState,
                _ => Task.FromResult(Result.Fail<string>(
                    new ConnectionError("keria", "Failed to fetch"))));
        }

        Assert.False(_broker.IsKeriaReachable);
        Assert.False(lastReachable);
    }

    [Fact]
    public async Task Reachability_SuccessResetsCounter() {
        // Two failures
        for (int i = 0; i < 2; i++) {
            await _broker.EnqueueReadAsync<string>(SignifyOperation.GetState,
                _ => Task.FromResult(Result.Fail<string>(
                    new ConnectionError("keria", "Failed to fetch"))));
        }

        // One success resets
        await _broker.EnqueueReadAsync(SignifyOperation.GetState,
            _ => Task.FromResult(Result.Ok("ok")));

        // Two more failures -- should not trigger unreachable (need 3 consecutive)
        for (int i = 0; i < 2; i++) {
            await _broker.EnqueueReadAsync<string>(SignifyOperation.GetState,
                _ => Task.FromResult(Result.Fail<string>(
                    new ConnectionError("keria", "Failed to fetch"))));
        }

        Assert.True(_broker.IsKeriaReachable);
    }

    [Fact]
    public async Task Reachability_NonNetworkErrors_DontAffectCounter() {
        for (int i = 0; i < 5; i++) {
            await _broker.EnqueueReadAsync<string>(SignifyOperation.GetState,
                _ => Task.FromResult(Result.Fail<string>(
                    new ValidationError("field", "invalid"))));
        }

        Assert.True(_broker.IsKeriaReachable);
    }

    [Fact]
    public async Task Reachability_RecoverAfterUnreachable() {
        bool? lastReachable = null;
        _broker.KeriaReachabilityChanged += r => lastReachable = r;

        // Trigger unreachable
        for (int i = 0; i < 3; i++) {
            await _broker.EnqueueReadAsync<string>(SignifyOperation.GetState,
                _ => Task.FromResult(Result.Fail<string>(
                    new ConnectionError("keria", "Failed to fetch"))));
        }
        Assert.False(_broker.IsKeriaReachable);

        // One success recovers
        await _broker.EnqueueReadAsync(SignifyOperation.GetState,
            _ => Task.FromResult(Result.Ok("ok")));

        Assert.True(_broker.IsKeriaReachable);
        Assert.True(lastReachable);
    }

    [Fact]
    public async Task EnqueueReadAsync_OperationException_ReturnsFailResult() {
        var result = await _broker.EnqueueReadAsync<string>(SignifyOperation.GetState,
            _ => throw new InvalidOperationException("boom"));

        Assert.True(result.IsFailed);
        Assert.Contains("boom", result.Errors[0].Message);
    }

    [Fact]
    public async Task SequentialExecution_NoOverlap() {
        int concurrency = 0;
        int maxConcurrency = 0;
        var results = new List<Task<Result<int>>>();

        for (int i = 0; i < 5; i++) {
            var idx = i;
            results.Add(_broker.EnqueueReadAsync(SignifyOperation.GetState, async _ => {
                var current = Interlocked.Increment(ref concurrency);
                if (current > maxConcurrency)
                    Interlocked.Exchange(ref maxConcurrency, current);
                await Task.Delay(10);
                Interlocked.Decrement(ref concurrency);
                return Result.Ok(idx);
            }));
        }

        await Task.WhenAll(results);

        Assert.Equal(1, maxConcurrency);
        Assert.True(results.All(r => r.Result.IsSuccess));
    }
}
