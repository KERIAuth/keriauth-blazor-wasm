namespace Extension.Services.NotificationPollingService;

public interface INotificationPollingService {
    Task StartPollingAsync(CancellationToken ct);
    Task PollOnDemandAsync();
}
