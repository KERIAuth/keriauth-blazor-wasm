namespace Extension.Services.NotificationPollingService;

using Extension.Helper;
using Extension.Models;
using Extension.Models.Storage;
using FluentResults;
using Extension.Services.SignifyBroker;
using Extension.Services.SignifyService.Models;
using Extension.Services.Storage;
using Microsoft.Extensions.Logging;

public class NotificationPollingService : INotificationPollingService {
    private readonly ISignifyRequestBroker _broker;
    private readonly IStorageGateway _storageGateway;
    private readonly ILogger<NotificationPollingService> _logger;

    // Tracks exchangeSaids already fetched in the current burst, to avoid redundant broker calls
    // and repeated OnSchemasNeeded invocations. Cleared at burst start.
    private readonly HashSet<string> _fetchedExchangeSaids = [];

    private static readonly HashSet<string> CredentialAffectingRoutes = ["/exn/ipex/grant", "/exn/ipex/admit"];

    // Fingerprint of the last notification list written to storage, to avoid unconditional writes every poll cycle
    private string? _lastNotificationFingerprint;
    private string? _lastCredentialFingerprint;

    // Single-flight gate: ensures only one PollOnDemandAsync runs at a time. Concurrent callers (Chrome alarm,
    // burst loop, IPEX/credential event triggers) skip if a poll is already in flight, so the in-flight cycle
    // (notifications + all uncached exns) completes before the next cycle begins.
    private readonly SemaphoreSlim _pollGate = new(1, 1);

    public Func<Task>? OnCredentialNotificationsChanged { get; set; }
    public Func<Task>? OnSchemasNeeded { get; set; }

    public NotificationPollingService(
        ISignifyRequestBroker broker,
        IStorageGateway storageGateway,
        ILogger<NotificationPollingService> logger) {
        _broker = broker;
        _storageGateway = storageGateway;
        _logger = logger;
    }

    public void Dispose() {
        _pollGate.Dispose();
        GC.SuppressFinalize(this);
    }

    public void ResetCacheState() {
        _lastNotificationFingerprint = null;
        _lastCredentialFingerprint = null;
        _logger.LogDebug(nameof(ResetCacheState) + ": Cleared notification/credential fingerprints");
    }

    public async Task StartPollingAsync(CancellationToken ct) {
        // Notification/credential fingerprints intentionally persist across bursts so steady-state
        // polling doesn't re-write unchanged CachedNotifications on every burst restart (each write
        // fires AppCache.Changed → StateHasChanged on every subscribed page). Use ResetCacheState()
        // to invalidate when storage may have been cleared (e.g., session lock).
        _fetchedExchangeSaids.Clear();
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
        // Non-blocking try-acquire: if a poll is already in flight, skip this invocation.
        // Subsequent triggers (alarm, burst loop, IPEX events) will run after the in-flight poll completes.
        if (!await _pollGate.WaitAsync(0)) {
            _logger.LogDebug(nameof(PollOnDemandAsync) + ": Skipped — poll already in progress");
            return;
        }
        try {
            await PollOnDemandInternalAsync();
        }
        finally {
            _pollGate.Release();
        }
    }

    private async Task PollOnDemandInternalAsync() {
        var result = await _broker.EnqueueBackgroundAsync(
            SignifyOperation.ListNotifications, svc => svc.ListNotifications());
        if (result.IsFailed) {
            // not_connected is expected during startup before connect completes — don't warn at 5s intervals
            var isNotConnected = result.Errors.Any(e => e is NotConnectedError);
            if (isNotConnected) {
                _logger.LogDebug(nameof(PollOnDemandAsync) + ": ListNotifications skipped — not connected");
            }
            else {
                _logger.LogWarning(nameof(PollOnDemandAsync) + ": ListNotifications failed: {Error}",
                    result.Errors.Count > 0 ? result.Errors[0].Message : "unknown");
            }
            return;
        }

        var notifications = new List<Notification>();
        foreach (var notifDict in result.Value) {
            var notification = ParseNotification(notifDict);
            if (notification is not null) {
                notifications.Add(notification);
            }
        }

        // Only write to storage when notifications actually changed, to avoid triggering
        // chrome.storage.onChanged → appCache subscription → StateHasChanged on every poll cycle.
        // Sender/target prefixes are derived from CachedExns on the read side (AppCache.GetExchangePrefixes),
        // so the fingerprint only needs id/read state. Sort by Id so KERIA response ordering doesn't
        // cause spurious fingerprint changes.
        var fingerprint = string.Join("|",
            notifications.Select(n => $"{n.Id}:{n.IsRead}").OrderBy(s => s, StringComparer.Ordinal));
        var fingerprintChanged = fingerprint != _lastNotificationFingerprint;
        if (fingerprintChanged) {
            // Only log the summary on actual change, not every 5s poll. Per-unread enumeration is at
            // Debug (high frequency × many unread = log noise that hurts BW perf via console.log).
            var unreadCount = notifications.Count(n => !n.IsRead);
            _logger.LogInformation(nameof(PollOnDemandAsync) + ": {Total} notifications ({Unread} unread)",
                notifications.Count, unreadCount);
            if (_logger.IsEnabled(LogLevel.Debug)) {
                foreach (var n in notifications.Where(n => !n.IsRead)) {
                    _logger.LogDebug(nameof(PollOnDemandAsync) + ": Unread: id={Id} dt={DateTime} route={Route} said={Said}",
                        n.Id, n.DateTime, n.Route, n.ExchangeSaid);
                }
            }
            _lastNotificationFingerprint = fingerprint;
            var stored = new CachedNotifications { Items = notifications };
            var psResult = await _storageGateway.GetItem<PollingState>(StorageArea.Session);
            var ps = psResult.IsSuccess && psResult.Value is not null ? psResult.Value : new PollingState();
            await _storageGateway.WriteTransaction(StorageArea.Session, tx => {
                tx.SetItem(stored);
                tx.SetItem(ps with { NotificationsLastFetchedUtc = DateTime.UtcNow });
            });
        }

        // Read CachedExns ONCE per poll instead of once per notification. Previously each iteration
        // called GetItem<CachedExns>(Session) — for 76+ notifications that's 76+ chrome.storage reads
        // of the full dict per first-poll-of-burst, which kept BW busy for ~1s and contended for the
        // chrome.storage subsystem. With one read up front, subsequent iterations are O(1) HashSet
        // lookups in memory.
        var cachedExnsResult = await _storageGateway.GetItem<CachedExns>(StorageArea.Session);
        var cachedSaids = cachedExnsResult.IsSuccess && cachedExnsResult.Value is not null
            ? new HashSet<string>(cachedExnsResult.Value.Exchanges.Keys)
            : [];

        // Populate CachedExns for any notifications whose exchange isn't yet cached.
        // The App reads sender/target prefixes reactively from CachedExns, so no second notifications write is needed.
        // Fetch newest notifications first so the UI (which sorts desc by DateTime) resolves prefixes top-down.
        // DateTime is ISO-8601, which sorts lexicographically in chronological order.
        foreach (var n in notifications.OrderByDescending(n => n.DateTime, StringComparer.Ordinal)) {
            await EnsureExchangeCachedAsync(n, cachedSaids);
        }

        if (fingerprintChanged && OnCredentialNotificationsChanged is not null) {
            var credentialFingerprint = string.Join("|",
                notifications
                    .Where(n => CredentialAffectingRoutes.Contains(n.Route))
                    .Select(n => $"{n.Id}:{n.IsRead}")
                    .OrderBy(s => s, StringComparer.Ordinal));
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
    /// Ensures the exchange referenced by the notification is present in CachedExns (session storage).
    /// Fetches and caches on miss; skips if any entry exists for the said. Also triggers
    /// background schema resolution for grant/offer exchanges the first time they're seen.
    ///
    /// Sender/target prefixes are derived from CachedExns on the read side via AppCache.GetExchangePrefixes,
    /// so this method intentionally does not track prefixes itself.
    ///
    /// === Why we do NOT auto-retry persistent failures (sentinel entries) ===
    /// CachedExns entries can be either real raw JSON (success) or empty-string sentinels
    /// (persistent failure — see StoreExchangeSentinelAsync). We treat both as "tried, skip"
    /// using ContainsKey rather than checking the value. Auto-retrying sentinels every burst
    /// would cost one wasted broker call per persistently-failing exn per burst — a "persistent"
    /// failure already means signify-ts internal retries exhausted and KERIA returned an error,
    /// so polling-driven retry is unlikely to succeed and just adds load.
    ///
    /// Recovery paths that DO retry sentinels:
    ///   1. User expands the notification — UI cache-first read deserializes empty as null and
    ///      falls through to the RequestGetExchange RPC, giving an on-demand retry.
    ///   2. Session lock+unlock — SessionManager.ClearKeriaSessionRecordsAsync wipes CachedExns,
    ///      so the next burst retries everything.
    ///
    /// KERI exchange (exn) message fields — see KERIpy serdering.py (search Ilks.exn):
    ///   https://github.com/WebOfTrust/keripy/blob/main/src/keri/core/serdering.py
    /// IPEX spec: https://www.ietf.org/archive/id/draft-ssmith-ipex-00.html
    /// KERIA IPEX endpoints: https://github.com/WebOfTrust/keria/blob/main/src/keria/app/ipexing.py
    /// Exchange response wrapper: { exn: { v, t, d, i, rp, p, dt, r, q, a, e }, pathed: {...} }
    ///   d=SAID, i=sender AID prefix, rp=recipient prefix, dt=datetime,
    ///   r=route (e.g. /ipex/grant), a=attributes, e=embedded data
    /// </summary>
    /// <param name="notification">The notification whose ExchangeSaid should be ensured-cached.</param>
    /// <param name="cachedSaids">Snapshot of CachedExns keys read once at the top of the poll.
    /// Updated in place when this method writes a new entry, so later iterations see the addition
    /// without another storage read.</param>
    private async Task EnsureExchangeCachedAsync(Notification notification, HashSet<string> cachedSaids) {
        if (notification.ExchangeSaid is null || !_fetchedExchangeSaids.Add(notification.ExchangeSaid)) {
            return;
        }
        // ContainsKey covers both real cached JSON and persistent-failure sentinels — see XML doc above
        // for why we deliberately don't auto-retry sentinels here.
        if (cachedSaids.Contains(notification.ExchangeSaid)) {
            TriggerSchemaResolutionIfNeeded(notification);
            return;
        }
        try {
            var rawResult = await _broker.EnqueueBackgroundAsync(
                SignifyOperation.GetExchangeRaw, svc => svc.GetExchangeRaw(notification.ExchangeSaid));
            if (rawResult.IsFailed) {
                var isTransient = rawResult.Errors.Any(e => e is NotConnectedError);
                _logger.LogWarning(nameof(EnsureExchangeCachedAsync) + ": GetExchange failed for {Said} (transient={Transient}): {Error}",
                    notification.ExchangeSaid, isTransient,
                    rawResult.Errors.Count > 0 ? rawResult.Errors[0].Message : "unknown");
                if (isTransient) {
                    // Allow retry on next poll — don't store a sentinel for connection-state errors
                    _fetchedExchangeSaids.Remove(notification.ExchangeSaid);
                }
                else {
                    // Persistent failure: store an empty-string sentinel so IsAllExnsCached doesn't block the UI forever.
                    await StoreExchangeSentinelAsync(notification.ExchangeSaid);
                    cachedSaids.Add(notification.ExchangeSaid);
                }
                return;
            }

            await AddToExchangeCacheAsync(notification.ExchangeSaid, rawResult.Value);
            cachedSaids.Add(notification.ExchangeSaid);
            TriggerSchemaResolutionIfNeeded(notification);
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, nameof(EnsureExchangeCachedAsync) + ": Exception fetching exchange {Said}", notification.ExchangeSaid);
            // Unknown exception — allow retry on next poll
            _fetchedExchangeSaids.Remove(notification.ExchangeSaid);
        }
    }

    /// <summary>
    /// Writes the freshly-fetched raw exchange JSON into CachedExns. Read-modify-write of the
    /// session storage record. Errors are logged at Debug — failure here just means the next poll
    /// re-fetches.
    /// </summary>
    private async Task AddToExchangeCacheAsync(string said, string rawJson) {
        try {
            var existing = await _storageGateway.GetItem<CachedExns>(StorageArea.Session);
            var exchanges = existing.IsSuccess && existing.Value is not null
                ? new Dictionary<string, string>(existing.Value.Exchanges)
                : [];
            exchanges[said] = rawJson;
            await _storageGateway.SetItem(new CachedExns { Exchanges = exchanges }, StorageArea.Session);
        }
        catch (Exception ex) {
            _logger.LogDebug(ex, nameof(AddToExchangeCacheAsync) + ": Cache write failed for {Said} (non-critical)", said);
        }
    }

    /// <summary>
    /// For grant/offer exchanges, fire-and-forget OnSchemasNeeded so KERIA can resolve any new
    /// schemas in the credential chain. Credentials are chained (e.g., ECR → ECR Auth → LE → QVI)
    /// and KERIA needs all schemas in the chain to verify and index a credential.
    /// Safe: callback's exceptions are caught and logged here; resolution doesn't block polling
    /// because unreachable schema hosts can hang for 30s+ each.
    /// </summary>
    private void TriggerSchemaResolutionIfNeeded(Notification notification) {
        if (OnSchemasNeeded is null) {
            return;
        }
        if (notification.Route is not ("/exn/ipex/grant" or "/exn/ipex/offer")) {
            return;
        }
        _ = Task.Run(async () => {
            try {
                await OnSchemasNeeded();
            }
            catch (Exception schemaEx) {
                _logger.LogWarning(schemaEx, nameof(TriggerSchemaResolutionIfNeeded) + ": Background schema resolution failed");
            }
        });
    }

    /// <summary>
    /// Writes an empty-string sentinel into CachedExns for a said whose fetch persistently failed.
    /// This marks the exchange as "resolved (failed)" so IsAllExnsCached can become true and the UI
    /// stops showing a loading spinner. Readers that encounter the empty value naturally fall through
    /// to on-demand RPC retries (deserialize yields null).
    /// Only written if there's no existing non-empty entry (so we never overwrite a real cache hit).
    /// </summary>
    private async Task StoreExchangeSentinelAsync(string said) {
        try {
            var existing = await _storageGateway.GetItem<CachedExns>(StorageArea.Session);
            var exchanges = existing.IsSuccess && existing.Value is not null
                ? new Dictionary<string, string>(existing.Value.Exchanges)
                : [];
            if (exchanges.TryGetValue(said, out var current) && !string.IsNullOrEmpty(current)) {
                return;
            }
            exchanges[said] = "";
            await _storageGateway.SetItem(new CachedExns { Exchanges = exchanges }, StorageArea.Session);
        }
        catch (Exception ex) {
            _logger.LogDebug(ex, nameof(StoreExchangeSentinelAsync) + ": Failed to write sentinel for {Said} (non-critical)", said);
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
