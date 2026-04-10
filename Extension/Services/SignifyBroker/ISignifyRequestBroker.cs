using FluentResults;
using Extension.Services.SignifyService;

namespace Extension.Services.SignifyBroker;

public interface ISignifyRequestBroker : IAsyncDisposable {
    // TODO P2: Consider per-key command ordering if concurrent credential operations on
    // different resources need independent serialization (e.g., CommandKey = credentialId).
    // Current single-channel approach is correct for the sequential BW call patterns.

    /// <summary>
    /// Enqueue a mutating operation (Connect, credential issue, IPEX flows).
    /// Processed with highest priority. Runs exclusively -- no other work executes concurrently.
    /// </summary>
    Task<Result<T>> EnqueueCommandAsync<T>(SignifyOperation op,
        Func<ISignifyClientService, Task<Result<T>>> operation,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueue a non-generic mutating operation (Disconnect, Ready, etc.).
    /// </summary>
    Task<Result> EnqueueCommandAsync(SignifyOperation op,
        Func<ISignifyClientService, Task<Result>> operation,
        CancellationToken ct = default);

    // TODO P2: Consider in-flight read deduplication via optional dedupKey parameter.
    // Concurrent reads with the same key would share one in-flight task instead of
    // queuing separately. Not needed yet -- current problems are contention-based.

    /// <summary>
    /// Enqueue an idempotent read (GetIdentifiers, GetCredentials, GetSchema).
    /// Processed when no commands are pending.
    /// </summary>
    Task<Result<T>> EnqueueReadAsync<T>(SignifyOperation op,
        Func<ISignifyClientService, Task<Result<T>>> operation,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueue a non-generic idempotent read (HealthCheck, Ready, etc.).
    /// </summary>
    Task<Result> EnqueueReadAsync(SignifyOperation op,
        Func<ISignifyClientService, Task<Result>> operation,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueue a low-priority background operation (polling, schema resolution, cache refresh).
    /// Yields to commands and reads. Skipped when PrioritizeInteractive is active.
    /// </summary>
    Task<Result<T>> EnqueueBackgroundAsync<T>(SignifyOperation op,
        Func<ISignifyClientService, Task<Result<T>>> operation,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueue a non-generic low-priority background operation.
    /// </summary>
    Task<Result> EnqueueBackgroundAsync(SignifyOperation op,
        Func<ISignifyClientService, Task<Result>> operation,
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
    /// While active, background channel items are held until the scope is disposed.
    /// </summary>
    IDisposable PrioritizeInteractive();
}
