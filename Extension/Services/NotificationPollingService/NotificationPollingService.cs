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

    // Track which notification IDs we've already logged exchange details for, to avoid repeated KERIA fetches
    private readonly HashSet<string> _loggedExchangeIds = [];

    // Fingerprint of the last notification list written to storage, to avoid unconditional writes every poll cycle
    private string? _lastNotificationFingerprint;

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
            // TODO P1: This was LogDebug, but silent failures here hide broken connections. Consider whether Warning is too noisy at 5s intervals.
            _logger.LogWarning(nameof(PollOnDemandAsync) + ": ListNotifications failed: {Error}",
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
            foreach (var n in notifications.Where(n => !n.IsRead)) {
                _logger.LogInformation(nameof(PollOnDemandAsync) + ": Unread: id={Id} dt={DateTime} route={Route} exchangeSaid={ExchangeSaid}",
                    n.Id, n.DateTime, n.Route, n.ExchangeSaid);
                await LogExchangeDetailsOnceAsync(n);
            }
        }

        // Only write to storage when notifications actually changed, to avoid triggering
        // chrome.storage.onChanged → appCache subscription → StateHasChanged on every poll cycle
        var fingerprint = string.Join("|", notifications.Select(n => $"{n.Id}:{n.IsRead}"));
        if (fingerprint == _lastNotificationFingerprint) return;
        _lastNotificationFingerprint = fingerprint;

        var stored = new Notifications { Items = notifications };
        await _storageService.SetItem(stored, StorageArea.Session);
    }

    /// <summary>
    /// Fetches and logs the exchange message details for a notification, but only once per exchangeSaid.
    /// </summary>
    private async Task LogExchangeDetailsOnceAsync(Notification notification) {
        if (notification.ExchangeSaid is null || !_loggedExchangeIds.Add(notification.ExchangeSaid)) {
            return;
        }
        try {
            var exnResult = await _signifyClient.GetExchange(notification.ExchangeSaid);
            if (exnResult.IsFailed) {
                _logger.LogWarning(nameof(LogExchangeDetailsOnceAsync) + ": GetExchange failed for {Said}: {Error}",
                    notification.ExchangeSaid, exnResult.Errors.Count > 0 ? exnResult.Errors[0].Message : "unknown");
                _loggedExchangeIds.Remove(notification.ExchangeSaid);
                return;
            }
            var exn = exnResult.Value;
            exn.TryGetValue("i", out var senderVal);
            exn.TryGetValue("a", out var aVal);
            string? recipient = null;
            if (aVal?.Dictionary is RecursiveDictionary aDict) {
                aDict.TryGetValue("i", out var recipientVal);
                recipient = recipientVal?.StringValue;
            }
            _logger.LogInformation(nameof(LogExchangeDetailsOnceAsync) + ": Exchange {Said}: sender={Sender} recipient={Recipient} route={Route}",
                notification.ExchangeSaid, senderVal?.StringValue, recipient, notification.Route);
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, nameof(LogExchangeDetailsOnceAsync) + ": Exception fetching exchange {Said}", notification.ExchangeSaid);
            _loggedExchangeIds.Remove(notification.ExchangeSaid);
        }
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
