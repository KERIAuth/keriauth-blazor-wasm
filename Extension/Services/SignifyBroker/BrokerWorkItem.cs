using FluentResults;
using Extension.Services.SignifyService;
using Extension.Models;

namespace Extension.Services.SignifyBroker;

internal enum BrokerPriority {
    Command,
    Read,
    Background
}

internal sealed class BrokerWorkItem {
    public string OperationName { get; }
    public BrokerPriority Priority { get; }
    public CancellationToken CancellationToken { get; }

    private readonly Func<ISignifyClientService, Task<Result<object?>>> _execute;
    private readonly TaskCompletionSource<Result<object?>> _tcs;

    public Task<Result<object?>> Task => _tcs.Task;

    public BrokerWorkItem(
        string operationName,
        BrokerPriority priority,
        Func<ISignifyClientService, Task<Result<object?>>> execute,
        CancellationToken ct) {
        OperationName = operationName;
        Priority = priority;
        CancellationToken = ct;
        _execute = execute;
        _tcs = new TaskCompletionSource<Result<object?>>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// Executes the operation and returns the result WITHOUT signaling the TCS.
    /// The caller (drain loop) must call Complete() after tracking the result.
    /// </summary>
    public async Task<Result<object?>?> ExecuteAsync(ISignifyClientService client) {
        if (CancellationToken.IsCancellationRequested) {
            _tcs.TrySetCanceled(CancellationToken);
            return null;
        }

        try {
            return await _execute(client);
        }
        catch (OperationCanceledException ex) {
            _tcs.TrySetCanceled(ex.CancellationToken);
            return null;
        }
        catch (Exception ex) {
            return Result.Fail<object?>(
                new JavaScriptInteropError(OperationName, $"Unhandled exception: {ex.Message}", ex));
        }
    }

    /// <summary>
    /// Signals the TCS with the given result, waking the caller.
    /// Called by the drain loop after reachability tracking.
    /// </summary>
    public void Complete(Result<object?> result) {
        _tcs.TrySetResult(result);
    }

    public void Cancel() {
        _tcs.TrySetCanceled();
    }
}

// Avoid boxing: helper to adapt typed Func delegates into the object?-based work item
internal static class BrokerWorkItemFactory {
    public static BrokerWorkItem Create<T>(
        string operationName,
        BrokerPriority priority,
        Func<ISignifyClientService, Task<Result<T>>> operation,
        CancellationToken ct) {
        return new BrokerWorkItem(
            operationName,
            priority,
            async svc => {
                var result = await operation(svc);
                return result.IsSuccess
                    ? Result.Ok<object?>(result.Value)
                    : Result.Fail<object?>(result.Errors);
            },
            ct);
    }

    public static BrokerWorkItem CreateNonGeneric(
        string operationName,
        BrokerPriority priority,
        Func<ISignifyClientService, Task<Result>> operation,
        CancellationToken ct) {
        return new BrokerWorkItem(
            operationName,
            priority,
            async svc => {
                var result = await operation(svc);
                return result.IsSuccess
                    ? Result.Ok<object?>(null)
                    : Result.Fail<object?>(result.Errors);
            },
            ct);
    }
}
