namespace Extension.Services.NotificationPollingService;

public interface INotificationPollingService : IDisposable {
    Task StartPollingAsync(CancellationToken ct);
    Task PollOnDemandAsync();

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
