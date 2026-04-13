namespace Extension.Services.NotificationPollingService;

public interface INotificationPollingService : IDisposable {
    Task StartPollingAsync(CancellationToken ct);
    Task PollOnDemandAsync();

    /// <summary>
    /// Invalidates in-memory dedup state (notification/credential fingerprints) so the next poll
    /// will re-write CachedNotifications even if the data appears unchanged. Call when storage
    /// records may have been cleared independently of this service (e.g., session lock). Without
    /// this, the in-memory fingerprint would prevent a re-write after storage is cleared.
    /// </summary>
    void ResetCacheState();

    /// <summary>
    /// Callback invoked when credential-affecting notifications change
    /// (routes: /exn/ipex/grant, /exn/ipex/admit).
    /// </summary>
    Func<Task>? OnCredentialNotificationsChanged { get; set; }

    /// <summary>
    /// Callback invoked when a grant/offer exchange is seen, to ensure all known schemas
    /// are resolved in KERIA. Credentials are chained, so the full schema chain must be available.
    /// Best-effort, non-blocking.
    /// </summary>
    Func<Task>? OnSchemasNeeded { get; set; }
}
