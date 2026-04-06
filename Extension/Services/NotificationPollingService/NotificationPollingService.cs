namespace Extension.Services.NotificationPollingService;

using Extension.Helper;
using Extension.Models.Storage;
using Extension.Services.SignifyService;
using Extension.Services.SignifyService.Models;
using Extension.Services.Storage;
using Microsoft.Extensions.Logging;

public class NotificationPollingService : INotificationPollingService {
    private readonly ISignifyClientService _signifyClient;
    private readonly IStorageGateway _storageGateway;
    private readonly ILogger<NotificationPollingService> _logger;

    // Cache exchange prefix data (issuer/recipient) to avoid repeated KERIA fetches
    private readonly Dictionary<string, (string? Issuer, string? Recipient)> _exchangePrefixCache = [];

    private static readonly HashSet<string> CredentialAffectingRoutes = ["/exn/ipex/grant", "/exn/ipex/admit"];

    // Fingerprint of the last notification list written to storage, to avoid unconditional writes every poll cycle
    private string? _lastNotificationFingerprint;
    private string? _lastCredentialFingerprint;

    public Func<Task>? OnCredentialNotificationsChanged { get; set; }
    public Func<Task>? OnSchemasNeeded { get; set; }
    public Func<bool>? IsClientReady { get; set; }
    public Func<bool>? IsLongOperationActive { get; set; }

    public NotificationPollingService(
        ISignifyClientService signifyClient,
        IStorageGateway storageGateway,
        ILogger<NotificationPollingService> logger) {
        _signifyClient = signifyClient;
        _storageGateway = storageGateway;
        _logger = logger;
    }

    public async Task StartPollingAsync(CancellationToken ct) {
        _lastNotificationFingerprint = null;
        _lastCredentialFingerprint = null;
        var deadline = DateTime.UtcNow + AppConfig.NotificationBurstDuration;
        _logger.LogInformation(nameof(StartPollingAsync) + ": Starting burst polling (interval={Interval}s, duration={Duration}s)",
            AppConfig.NotificationPollInterval.TotalSeconds, AppConfig.NotificationBurstDuration.TotalSeconds);
        while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline) {
            try {
                await PollOnDemandAsync();
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, nameof(StartPollingAsync) + ": Error during notification poll");
            }

            try {
                await Task.Delay(AppConfig.NotificationPollInterval, ct);
            }
            catch (OperationCanceledException) {
                break;
            }
        }
        _logger.LogInformation(nameof(StartPollingAsync) + ": Burst polling stopped");
    }

    public async Task PollOnDemandAsync() {
        if (IsClientReady is not null && !IsClientReady()) {
            _logger.LogDebug(nameof(PollOnDemandAsync) + ": Skipped — signify client not ready (no passcode)");
            return;
        }

        if (IsLongOperationActive is not null && IsLongOperationActive()) {
            _logger.LogDebug(nameof(PollOnDemandAsync) + ": Skipped — long signify operation in progress");
            return;
        }

        var result = await _signifyClient.ListNotifications();
        if (result.IsFailed) {
            // TODO P3: This was LogDebug, but silent failures here hide broken connections. Consider whether Warning is too noisy at 5s intervals.
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

        // Fetch and cache exchange prefix data for all notifications
        foreach (var n in notifications) {
            await FetchAndCacheExchangePrefixesAsync(n);
        }

        // Enrich notifications with cached prefix data
        notifications = notifications.Select(n => {
            if (n.ExchangeSaid is not null && _exchangePrefixCache.TryGetValue(n.ExchangeSaid, out var prefixes)) {
                return new Notification {
                    Id = n.Id,
                    DateTime = n.DateTime,
                    IsRead = n.IsRead,
                    Route = n.Route,
                    ExchangeSaid = n.ExchangeSaid,
                    SenderPrefix = prefixes.Issuer,
                    TargetPrefix = prefixes.Recipient
                };
            }
            return n;
        }).ToList();

        var unreadCount = notifications.Count(n => !n.IsRead);
        if (unreadCount > 0) {
            _logger.LogInformation(nameof(PollOnDemandAsync) + ": {Total} notifications ({Unread} unread)",
                notifications.Count, unreadCount);
            foreach (var n in notifications.Where(n => !n.IsRead)) {
                _logger.LogInformation(nameof(PollOnDemandAsync) + ": Unread: id={Id} dt={DateTime} route={Route} sender={Sender} target={Target}",
                    n.Id, n.DateTime, n.Route, n.SenderPrefix, n.TargetPrefix);
            }
        }

        // Only write to storage when notifications actually changed, to avoid triggering
        // chrome.storage.onChanged → appCache subscription → StateHasChanged on every poll cycle
        var fingerprint = string.Join("|", notifications.Select(n => $"{n.Id}:{n.IsRead}:{n.SenderPrefix}:{n.TargetPrefix}"));
        if (fingerprint == _lastNotificationFingerprint) return;
        _lastNotificationFingerprint = fingerprint;

        var stored = new CachedNotifications { Items = notifications };
        var psResult = await _storageGateway.GetItem<PollingState>(StorageArea.Session);
        var ps = psResult.IsSuccess && psResult.Value is not null ? psResult.Value : new PollingState();
        await _storageGateway.WriteTransaction(StorageArea.Session, tx => {
            tx.SetItem(stored);
            tx.SetItem(ps with { NotificationsLastFetchedUtc = DateTime.UtcNow });
        });

        if (OnCredentialNotificationsChanged is not null) {
            var credentialFingerprint = string.Join("|",
                notifications
                    .Where(n => CredentialAffectingRoutes.Contains(n.Route))
                    .Select(n => $"{n.Id}:{n.IsRead}"));
            if (credentialFingerprint != _lastCredentialFingerprint) {
                _lastCredentialFingerprint = credentialFingerprint;
                try {
                    await OnCredentialNotificationsChanged();
                }
                catch (Exception ex) {
                    _logger.LogWarning(ex, nameof(PollOnDemandAsync) + ": Error in OnCredentialNotificationsChanged callback");
                }
            }
        }
    }

    /// <summary>
    /// Updates NotificationsLastFetchedUtc on PollingState after a successful fetch.
    /// </summary>
    private async Task UpdateNotificationsLastFetchedAsync() {
        try {
            var result = await _storageGateway.GetItem<PollingState>(StorageArea.Session);
            var current = result.IsSuccess && result.Value is not null ? result.Value : new PollingState();
            await _storageGateway.SetItem(current with { NotificationsLastFetchedUtc = DateTime.UtcNow }, StorageArea.Session);
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, nameof(UpdateNotificationsLastFetchedAsync) + ": Failed to update polling timestamp");
        }
    }

    /// <summary>
    /// Fetches exchange data and caches the issuer/recipient prefixes, once per exchangeSaid.
    /// </summary>
    private async Task FetchAndCacheExchangePrefixesAsync(Notification notification) {
        if (notification.ExchangeSaid is null || _exchangePrefixCache.ContainsKey(notification.ExchangeSaid)) {
            return;
        }
        try {
            var exnResult = await _signifyClient.GetExchange(notification.ExchangeSaid);
            if (exnResult.IsFailed) {
                _logger.LogWarning(nameof(FetchAndCacheExchangePrefixesAsync) + ": GetExchange failed for {Said}: {Error}",
                    notification.ExchangeSaid, exnResult.Errors.Count > 0 ? exnResult.Errors[0].Message : "unknown");
                return;
            }
            // KERI exchange (exn) message fields — see KERIpy serdering.py (search Ilks.exn):
            //   https://github.com/WebOfTrust/keripy/blob/main/src/keri/core/serdering.py
            // IPEX spec: https://www.ietf.org/archive/id/draft-ssmith-ipex-00.html
            // KERIA IPEX endpoints: https://github.com/WebOfTrust/keria/blob/main/src/keria/app/ipexing.py
            // Exchange response wrapper: { exn: { v, t, d, i, rp, p, dt, r, q, a, e }, pathed: {...} }
            //   d=SAID, i=sender AID prefix, rp=recipient prefix, dt=datetime,
            //   r=route (e.g. /ipex/grant), a=attributes, e=embedded data
            var view = ExchangeView.FromRecursiveDictionary(exnResult.Value);
            _exchangePrefixCache[notification.ExchangeSaid] = (view.I, view.Rp);
            _logger.LogInformation(nameof(FetchAndCacheExchangePrefixesAsync) + ": Exchange {Said}: sender={Sender} recipient={Recipient} route={Route}",
                notification.ExchangeSaid, view.I, view.Rp, notification.Route);

            // For grant/offer exchanges, proactively resolve all known schemas in the background.
            // Credentials are chained (e.g., ECR → ECR Auth → LE → QVI) and KERIA needs all schemas
            // in the chain to verify and index a credential.
            // Fire-and-forget: don't block notification polling while resolving (unreachable hosts can timeout for 30s+ each).
            if (OnSchemasNeeded is not null &&
                notification.Route is "/exn/ipex/grant" or "/exn/ipex/offer") {
                _ = Task.Run(async () => {
                    try {
                        await OnSchemasNeeded();
                    }
                    catch (Exception schemaEx) {
                        _logger.LogWarning(schemaEx, nameof(FetchAndCacheExchangePrefixesAsync) +
                            ": Background schema resolution failed");
                    }
                });
            }
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, nameof(FetchAndCacheExchangePrefixesAsync) + ": Exception fetching exchange {Said}", notification.ExchangeSaid);
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
