namespace Extension.Services.NotificationPollingService;

using Extension.Helper;
using Extension.Models.Storage;
using Extension.Services.SignifyService;
using Extension.Services.SignifyService.Models;
using Extension.Services.Storage;
using Microsoft.Extensions.Logging;

public class NotificationPollingService : INotificationPollingService {
    private readonly ISignifyClientService _signifyClient;
    private readonly IStorageService _storageService;
    private readonly ILogger<NotificationPollingService> _logger;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    public NotificationPollingService(
        ISignifyClientService signifyClient,
        IStorageService storageService,
        ILogger<NotificationPollingService> logger) {
        _signifyClient = signifyClient;
        _storageService = storageService;
        _logger = logger;
    }

    public async Task StartPollingAsync(CancellationToken ct) {
        _logger.LogInformation(nameof(StartPollingAsync) + ": Starting notification polling (interval={Interval}s)", PollInterval.TotalSeconds);
        while (!ct.IsCancellationRequested) {
            try {
                await PollOnDemandAsync();
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, nameof(StartPollingAsync) + ": Error during notification poll");
            }

            try {
                await Task.Delay(PollInterval, ct);
            }
            catch (OperationCanceledException) {
                break;
            }
        }
        _logger.LogInformation(nameof(StartPollingAsync) + ": Notification polling stopped");
    }

    public async Task PollOnDemandAsync() {
        var result = await _signifyClient.ListNotifications();
        if (result.IsFailed) {
            _logger.LogDebug(nameof(PollOnDemandAsync) + ": ListNotifications failed: {Error}",
                result.Errors.Count > 0 ? result.Errors[0].Message : "unknown");
            return;
        }

        var notifications = new List<Notification>();
        foreach (var notifDict in result.Value) {
            var notification = ParseNotification(notifDict);
            if (notification is not null) {
                notifications.Add(notification);
            }
        }

        var unreadCount = notifications.Count(n => !n.IsRead);
        if (unreadCount > 0) {
            _logger.LogInformation(nameof(PollOnDemandAsync) + ": {Total} notifications ({Unread} unread)",
                notifications.Count, unreadCount);
        }

        var stored = new Notifications { Items = notifications };
        await _storageService.SetItem(stored, StorageArea.Session);
    }

    private Notification? ParseNotification(RecursiveDictionary notifDict) {
        try {
            notifDict.TryGetValue("i", out var iVal);
            notifDict.TryGetValue("dt", out var dtVal);
            notifDict.TryGetValue("r", out var rVal);

            var id = iVal?.StringValue;
            var dt = dtVal?.StringValue;
            var isRead = rVal?.BooleanValue == true;

            if (id is null || dt is null) return null;

            string? route = null;
            string? exchangeSaid = null;

            if (notifDict.TryGetValue("a", out var aVal) && aVal.Dictionary is RecursiveDictionary aDict) {
                aDict.TryGetValue("r", out var routeVal);
                aDict.TryGetValue("d", out var dVal);
                route = routeVal?.StringValue;
                exchangeSaid = dVal?.StringValue;
            }

            return new Notification {
                Id = id,
                DateTime = dt,
                IsRead = isRead,
                Route = route ?? "",
                ExchangeSaid = exchangeSaid
            };
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, nameof(ParseNotification) + ": Failed to parse notification");
            return null;
        }
    }
}
