using FluentResults;
using Extension.Services.SignifyService;

namespace Extension.Services.SignifyBroker;

public interface ISignifyRequestBroker : IAsyncDisposable {
    /// <summary>
    /// Enqueue a mutating operation (Connect, credential issue, IPEX flows).
    /// Processed with highest priority. Runs exclusively -- no other work executes concurrently.
    /// </summary>
    Task<Result<T>> EnqueueCommandAsync<T>(string opName,
        Func<ISignifyClientService, Task<Result<T>>> operation,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueue a non-generic mutating operation (Disconnect, Ready, etc.).
    /// </summary>
    Task<Result> EnqueueCommandAsync(string opName,
        Func<ISignifyClientService, Task<Result>> operation,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueue an idempotent read (GetIdentifiers, GetCredentials, GetSchema).
    /// Processed when no commands are pending.
    /// </summary>
    Task<Result<T>> EnqueueReadAsync<T>(string opName,
        Func<ISignifyClientService, Task<Result<T>>> operation,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueue a low-priority background operation (polling, schema resolution, cache refresh).
    /// Yields to commands and reads. Skipped when SuspendBackground is active.
    /// </summary>
    Task<Result<T>> EnqueueBackgroundAsync<T>(string opName,
        Func<ISignifyClientService, Task<Result<T>>> operation,
        CancellationToken ct = default);

    /// <summary>
    /// True when KERIA is believed reachable. Based on consecutive failure tracking
    /// (3 consecutive ConnectionError/NotConnectedError failures before signaling unreachable).
    /// </summary>
    bool IsKeriaReachable { get; }

    /// <summary>
    /// Fired when KERIA reachability changes.
    /// </summary>
    event Action<bool>? KeriaReachabilityChanged;

    /// <summary>
    /// Suspend background operations. Returns IDisposable that resumes on dispose.
    /// Use to prevent background work from interleaving with multi-step command sequences.
    /// Replaces BeginLongOperation.
    /// </summary>
    IDisposable SuspendBackground();
}
