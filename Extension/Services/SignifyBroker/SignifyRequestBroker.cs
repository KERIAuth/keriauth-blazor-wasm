using System.Threading.Channels;
using Extension.Models;
using Extension.Services.SignifyService;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace Extension.Services.SignifyBroker;

public sealed class SignifyRequestBroker : ISignifyRequestBroker {
    private readonly ISignifyClientService _client;
    private readonly ILogger<SignifyRequestBroker> _logger;
    private readonly CancellationTokenSource _shutdownCts = new();

    // Three priority channels
    private readonly Channel<BrokerWorkItem> _commandChannel =
        Channel.CreateUnbounded<BrokerWorkItem>(new UnboundedChannelOptions { SingleReader = true });

    private readonly Channel<BrokerWorkItem> _readChannel =
        Channel.CreateUnbounded<BrokerWorkItem>(new UnboundedChannelOptions { SingleReader = true });

    private readonly Channel<BrokerWorkItem> _backgroundChannel =
        Channel.CreateBounded<BrokerWorkItem>(new BoundedChannelOptions(16) {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest
        });

    // Drain loop task
    private readonly Task _drainLoop;

    // Background suspension
    private int _backgroundSuspendCount;
    private TaskCompletionSource _backgroundResumedSignal = new();

    // Reachability tracking
    private int _consecutiveFailures;
    private bool _isKeriaReachable = true;
    private const int UnreachableThreshold = 3;

    public bool IsKeriaReachable => _isKeriaReachable;
    public event Action<bool>? KeriaReachabilityChanged;

    public SignifyRequestBroker(ISignifyClientService client, ILogger<SignifyRequestBroker> logger) {
        _client = client;
        _logger = logger;
        _drainLoop = DrainLoopAsync(_shutdownCts.Token);
    }

    public async Task<Result<T>> EnqueueCommandAsync<T>(SignifyOperation op,
        Func<ISignifyClientService, Task<Result<T>>> operation,
        CancellationToken ct = default) {
        var item = BrokerWorkItemFactory.Create(op, BrokerPriority.Command, operation, ct);
        await _commandChannel.Writer.WriteAsync(item, ct);
        return await UnwrapResult<T>(item);
    }

    public async Task<Result> EnqueueCommandAsync(SignifyOperation op,
        Func<ISignifyClientService, Task<Result>> operation,
        CancellationToken ct = default) {
        var item = BrokerWorkItemFactory.CreateNonGeneric(op, BrokerPriority.Command, operation, ct);
        await _commandChannel.Writer.WriteAsync(item, ct);
        var result = await item.Task;
        return result.IsSuccess ? Result.Ok() : Result.Fail(result.Errors);
    }

    public async Task<Result<T>> EnqueueReadAsync<T>(SignifyOperation op,
        Func<ISignifyClientService, Task<Result<T>>> operation,
        CancellationToken ct = default) {
        var item = BrokerWorkItemFactory.Create(op, BrokerPriority.Read, operation, ct);
        await _readChannel.Writer.WriteAsync(item, ct);
        return await UnwrapResult<T>(item);
    }

    public async Task<Result> EnqueueReadAsync(SignifyOperation op,
        Func<ISignifyClientService, Task<Result>> operation,
        CancellationToken ct = default) {
        var item = BrokerWorkItemFactory.CreateNonGeneric(op, BrokerPriority.Read, operation, ct);
        await _readChannel.Writer.WriteAsync(item, ct);
        var result = await item.Task;
        return result.IsSuccess ? Result.Ok() : Result.Fail(result.Errors);
    }

    public async Task<Result<T>> EnqueueBackgroundAsync<T>(SignifyOperation op,
        Func<ISignifyClientService, Task<Result<T>>> operation,
        CancellationToken ct = default) {
        var item = BrokerWorkItemFactory.Create(op, BrokerPriority.Background, operation, ct);
        if (!_backgroundChannel.Writer.TryWrite(item)) {
            _logger.LogDebug("Background channel full, dropping oldest for {Op}", op);
            await _backgroundChannel.Writer.WriteAsync(item, ct);
        }
        return await UnwrapResult<T>(item);
    }

    public async Task<Result> EnqueueBackgroundAsync(SignifyOperation op,
        Func<ISignifyClientService, Task<Result>> operation,
        CancellationToken ct = default) {
        var item = BrokerWorkItemFactory.CreateNonGeneric(op, BrokerPriority.Background, operation, ct);
        if (!_backgroundChannel.Writer.TryWrite(item)) {
            _logger.LogDebug("Background channel full, dropping oldest for {Op}", op);
            await _backgroundChannel.Writer.WriteAsync(item, ct);
        }
        var result = await item.Task;
        return result.IsSuccess ? Result.Ok() : Result.Fail(result.Errors);
    }

    public IDisposable PrioritizeInteractive() {
        Interlocked.Increment(ref _backgroundSuspendCount);
        _logger.LogDebug("PrioritizeInteractive: count={Count}", _backgroundSuspendCount);
        return new InteractivePriorityScope(this);
    }

    private async Task DrainLoopAsync(CancellationToken ct) {
        _logger.LogInformation("SignifyRequestBroker drain loop started");
        try {
            while (!ct.IsCancellationRequested) {
                var item = await DequeueNextAsync(ct);
                if (item is null) continue;

                _logger.LogDebug("Executing {Priority} operation: {Op}",
                    item.Priority, item.Operation);

                var result = await item.ExecuteAsync(_client);
                if (result is not null) {
                    TrackResult(result, item.Operation);
                    item.Complete(result);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            _logger.LogInformation("SignifyRequestBroker drain loop stopped (shutdown)");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "SignifyRequestBroker drain loop failed unexpectedly");
        }
    }

    private async Task<BrokerWorkItem?> DequeueNextAsync(CancellationToken ct) {
        // Priority 1: Commands
        if (_commandChannel.Reader.TryRead(out var cmd))
            return cmd;

        // Priority 2: Reads
        if (_readChannel.Reader.TryRead(out var read))
            return read;

        // Priority 3: Background (only if not suspended)
        if (_backgroundSuspendCount == 0 && _backgroundChannel.Reader.TryRead(out var bg))
            return bg;

        // Nothing ready -- wait for any channel to have data
        var commandWait = _commandChannel.Reader.WaitToReadAsync(ct).AsTask();
        var readWait = _readChannel.Reader.WaitToReadAsync(ct).AsTask();

        if (_backgroundSuspendCount == 0) {
            var backgroundWait = _backgroundChannel.Reader.WaitToReadAsync(ct).AsTask();
            await Task.WhenAny(commandWait, readWait, backgroundWait);
        }
        else {
            // Background is suspended -- also wait for resume signal so we can
            // re-check when suspension is lifted
            await Task.WhenAny(commandWait, readWait, _backgroundResumedSignal.Task);
        }

        // After waking, re-check in priority order
        if (_commandChannel.Reader.TryRead(out cmd))
            return cmd;

        if (_readChannel.Reader.TryRead(out read))
            return read;

        if (_backgroundSuspendCount == 0 && _backgroundChannel.Reader.TryRead(out bg))
            return bg;

        return null;
    }

    private void TrackResult(Result<object?> result, SignifyOperation operation) {
        if (result.IsSuccess) {
            if (_consecutiveFailures > 0) {
                _logger.LogDebug("Reachability: reset after {Op} succeeded (was {Failures} consecutive failures)",
                    operation, _consecutiveFailures);
            }
            _consecutiveFailures = 0;
            SetReachable(true);
        }
        else if (result.Errors.Any(e => e is ConnectionError or NotConnectedError)) {
            _consecutiveFailures++;
            _logger.LogDebug("Reachability: {Failures}/{Threshold} consecutive failures after {Op}",
                _consecutiveFailures, UnreachableThreshold, operation);
            if (_consecutiveFailures >= UnreachableThreshold) {
                SetReachable(false);
            }
        }
        // Other error types (ValidationError, OperationTimeoutError, JavaScriptInteropError)
        // do not affect reachability -- they indicate application-level issues, not connectivity.
    }

    private void SetReachable(bool reachable) {
        if (_isKeriaReachable != reachable) {
            _isKeriaReachable = reachable;
            _logger.LogInformation("KERIA reachability changed to {Reachable}", reachable);
            KeriaReachabilityChanged?.Invoke(reachable);
        }
    }

    private static async Task<Result<T>> UnwrapResult<T>(BrokerWorkItem item) {
        var boxed = await item.Task;
        if (boxed.IsSuccess) {
            return boxed.Value is T typed
                ? Result.Ok(typed)
                : Result.Fail<T>("Broker: unexpected result type");
        }
        return Result.Fail<T>(boxed.Errors);
    }

    public async ValueTask DisposeAsync() {
        _commandChannel.Writer.TryComplete();
        _readChannel.Writer.TryComplete();
        _backgroundChannel.Writer.TryComplete();

        await _shutdownCts.CancelAsync();

        try {
            await _drainLoop.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException) {
            _logger.LogWarning("Drain loop did not stop within 5 seconds");
        }
        catch (OperationCanceledException) {
            // Expected
        }

        _shutdownCts.Dispose();
    }

    private sealed class InteractivePriorityScope(SignifyRequestBroker owner) : IDisposable {
        private int _disposed;
        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) {
                var newCount = Interlocked.Decrement(ref owner._backgroundSuspendCount);
                owner._logger.LogDebug("InteractivePriorityScope.Dispose: count={Count}", newCount);
                if (newCount == 0) {
                    // Signal the drain loop to re-check background channel
                    owner._backgroundResumedSignal.TrySetResult();
                    owner._backgroundResumedSignal = new TaskCompletionSource();
                }
            }
        }
    }
}
