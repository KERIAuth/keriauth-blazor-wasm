namespace Extension.Services.NotificationPollingService;

public interface INotificationPollingService {
    Task StartPollingAsync(CancellationToken ct);
    Task PollOnDemandAsync();

    /// <summary>
    /// Callback invoked when credential-affecting notifications change
    /// (routes: /exn/ipex/grant, /exn/ipex/admit).
    /// </summary>
    Func<Task>? OnCredentialNotificationsChanged { get; set; }
}
