using Extension.Models;
using Extension.Models.Storage;
using Extension.Services.Storage;
using Extension.Utilities;
using JsBind.Net;
using WebExtensions.Net;
using WebExtensions.Net.Alarms;

namespace Extension.Services;

/// <summary>
/// Manages session expiration lifecycle using a hybrid approach:
/// 1. In-memory passcode field (_passcode) — never written to any storage
/// 2. SessionStateModel in chrome.storage.session — contains only SessionExpirationUtc (not sensitive)
/// 3. One-shot Chrome Alarm scheduled to fire exactly at session expiration time
/// 4. Periodic keep-alive alarm to prevent service worker suspension during an active session
///
/// Responsibilities:
/// - Validates and stores passcode in memory via UnlockSessionAsync (called by BackgroundWorker)
/// - Updates SessionStateModel.SessionExpirationUtc immediately when session is extended or prefs change
/// - Schedules one-shot alarm to fire at exact expiration time (no polling)
/// - Schedules periodic keep-alive alarm while session is unlocked
/// - Clears expired sessions on SessionManager startup (forces lock if SW restarted mid-session)
/// - Clears session storage when alarm fires or session expires
/// - Validates session is unlocked: _passcode != null AND session not expired
/// - Provides public API for unlocking, extending, or locking sessions
///
/// SECURITY: The passcode is kept exclusively in this class's _passcode field.
/// If the service worker is force-restarted (sleep, crash, Chrome eviction), _passcode becomes null
/// and the session locks immediately upon the next activity. The user must re-authenticate.
/// </summary>
public class SessionManager : IDisposable {
    private readonly ILogger<SessionManager> _logger;
    private readonly IStorageService _storageService;
    private readonly IStorageGateway _storageGateway;
    private readonly WebExtensionsApi _webExtensionsApi;
    private const string unicodeLockIcon = "\U0001F512"; // Unicode lock icon 🔒
    public const string SessionKeepAliveAlarmName = "SessionKeepAliveAlarm";

    // In-memory passcode — never persisted to any storage
    private string? _passcode;

    // Storage observers - disposed on cleanup
    private IDisposable? _sessionStateObserver;
    private IDisposable? _preferencesObserver;

    /// <summary>
    /// Constructor starts SessionManager initialization asynchronously.
    /// Initialization runs in background to avoid blocking in browser/Blazor WASM context.
    /// Errors during initialization are logged but do not prevent construction.
    /// </summary>
    public SessionManager(
        ILogger<SessionManager> logger,
        IStorageService storageService,
        IStorageGateway storageGateway,
        IJsRuntimeAdapter jsRuntimeAdapter,
        bool isSessionOwner = true) {
        _logger = logger;
        _storageService = storageService;
        _storageGateway = storageGateway;
        _webExtensionsApi = new WebExtensionsApi(jsRuntimeAdapter);

        _logger.LogInformation(nameof(SessionManager) + ": initializing (isSessionOwner={IsSessionOwner})...", isSessionOwner);

        // Only the BW (session owner) runs startup checks and subscribes to storage changes.
        // The App also injects SessionManager but must not run startup logic — it has no passcode
        // in memory and would incorrectly clear the BW's valid SessionStateModel on every App open.
        if (isSessionOwner) {
            _ = Task.Run(async () => {
                try {
                    await StartAsync();
                    _logger.LogInformation(nameof(SessionManager) + ": initialized successfully");
                }
                catch (Exception ex) {
                    _logger.LogError(ex, nameof(SessionManager) + ": initialization failed");
                    // Don't rethrow - nobody awaits this task, so the exception would be lost
                }
            });
        }
    }

    /// <summary>
    /// Returns the in-memory passcode, or null if the session is locked.
    /// Used by BackgroundWorker to pass the passcode to signify-ts.
    /// </summary>
    public string? GetPasscode() => _passcode;

    /// <summary>
    /// Starts SessionManager: checks for stale SessionStateModel on startup, subscribes to storage changes.
    /// Throws exception on failure (fail-fast).
    /// </summary>
    private async Task StartAsync() {
        // 1. Lock immediately if SessionStateModel exists from a previous SW lifetime
        //    (_passcode is null after any SW restart, so the session cannot be resumed)
        await CheckAndClearExpiredSessionOnStartupAsync();

        // 2. Subscribe to storage changes for immediate reactivity
        SubscribeToStorageChanges();
    }

    /// <summary>
    /// Checks if SessionStateModel exists on startup.
    /// Because _passcode is null after any SW restart, any existing SessionStateModel
    /// means the session cannot be resumed — lock immediately.
    /// Handles: browser suspend/resume, service worker restart, chrome crash.
    /// </summary>
    private async Task CheckAndClearExpiredSessionOnStartupAsync() {
        try {
            var sessionStateRes = await _storageService.GetItem<SessionStateModel>(StorageArea.Session);

            if (sessionStateRes.IsFailed) {
                _logger.LogDebug(nameof(CheckAndClearExpiredSessionOnStartupAsync) + ": No SessionStateModel found on startup - session is locked");
                return;
            }

            var sessionState = sessionStateRes.Value;
            if (sessionState is null) {
                _logger.LogDebug(nameof(CheckAndClearExpiredSessionOnStartupAsync) + ": SessionStateModel is null on startup - session is locked");
                return;
            }

            // SessionStateModel exists but _passcode is null (SW was restarted).
            // Lock immediately — user must re-authenticate.
            _logger.LogWarning(nameof(CheckAndClearExpiredSessionOnStartupAsync) + ": SessionStateModel found but passcode lost (SW restarted) — LOCKING SESSION. Expiration was {Exp}",
                sessionState.SessionExpirationUtc);
            await LockSessionAsync();
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(CheckAndClearExpiredSessionOnStartupAsync) + ": Error checking session on startup");
            throw new InvalidOperationException("Failed to check session on startup", ex);
        }
    }

    /// <summary>
    /// Unlocks the session: validates the passcode hash, stores the passcode in memory,
    /// writes SessionStateModel to session storage, and starts alarms.
    /// Called by BackgroundWorker when the App sends an UnlockSession RPC.
    /// </summary>
    public async Task<FluentResults.Result> UnlockSessionAsync(string passcode) {
        try {
            // Validate passcode length
            if (string.IsNullOrEmpty(passcode) || passcode.Length != 21) {
                return FluentResults.Result.Fail("Invalid passcode: must be 21 characters");
            }

            // Get preferences to find the selected config digest
            var prefsRes = await _storageService.GetItem<Preferences>();
            if (prefsRes.IsFailed || prefsRes.Value is null) {
                return FluentResults.Result.Fail("Preferences not found");
            }

            var selectedDigest = prefsRes.Value.SelectedKeriaConnectionDigest;
            if (string.IsNullOrEmpty(selectedDigest)) {
                return FluentResults.Result.Fail("No KERIA configuration selected");
            }

            // Get the KeriaConnectConfigs dictionary
            var configsRes = await _storageService.GetItem<KeriaConnectConfigs>();
            if (configsRes.IsFailed || configsRes.Value is null || !configsRes.Value.IsStored) {
                return FluentResults.Result.Fail("KERIA configuration not found");
            }

            if (!configsRes.Value.Configs.TryGetValue(selectedDigest, out var selectedConfig)) {
                return FluentResults.Result.Fail("Selected KERIA configuration not found");
            }

            // Validate passcode hash
            var storedHash = selectedConfig.PasscodeHash;
            if (storedHash == 0) {
                return FluentResults.Result.Fail("PasscodeHash not set in configuration");
            }

            var currentHash = DeterministicHash.ComputeHash(passcode);
            if (currentHash != storedHash) {
                _logger.LogWarning(nameof(UnlockSessionAsync) + ": Passcode hash mismatch");
                return FluentResults.Result.Fail($"{AppConfig.ProductName} was not configured with this passcode on this browser profile.");
            }

            // Store passcode in memory only
            _passcode = passcode;

            // Write expiration to session storage (not sensitive)
            var timeoutMinutes = prefsRes.Value.InactivityTimeoutMinutes;
            var expirationUtc = DateTime.UtcNow.AddMinutes(timeoutMinutes);
            var setRes = await _storageService.SetItem<SessionStateModel>(
                new SessionStateModel { SessionExpirationUtc = expirationUtc },
                StorageArea.Session
            );
            if (setRes.IsFailed) {
                _passcode = null; // rollback
                return FluentResults.Result.Fail($"Failed to write session state: {setRes.Errors[0].Message}");
            }

            // Schedule expiration alarm and start keep-alive alarm
            await ScheduleExpirationAlarmAsync(expirationUtc);
            await EnsureKeepAliveAlarmAsync();
            await ClearLockIconAsync();

            _logger.LogInformation(nameof(UnlockSessionAsync) + ": Session unlocked, expiration={Expiration}", expirationUtc);
            return FluentResults.Result.Ok();
        }
        catch (Exception ex) {
            _passcode = null;
            _logger.LogError(ex, nameof(UnlockSessionAsync) + ": Exception during unlock");
            return FluentResults.Result.Fail($"Unlock failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Schedules a named one-shot Chrome Alarm to fire at the exact session expiration time.
    /// Replaces any existing alarm with the same name.
    /// Validates expiration time is in the future and within max timeout range.
    /// </summary>
    private async Task ScheduleExpirationAlarmAsync(DateTime newExpirationUtc) {
        _logger.LogDebug(nameof(ScheduleExpirationAlarmAsync) + ": Scheduling alarm for {Expiration} ???????", newExpirationUtc);
        try {
            var now = DateTime.UtcNow;
            var timeUntilExpiration = newExpirationUtc - now;

            // Validate expiration is in the future
            if (newExpirationUtc <= now) {
                throw new ArgumentException(
                    $"Expiration time {newExpirationUtc} must be in the future (now: {now})");
            }

            // Validate expiration is not too far in the future
            var maxTimeout = TimeSpan.FromMinutes(AppConfig.MaxInactivityTimeoutMins);
            if (timeUntilExpiration > maxTimeout) {
                throw new ArgumentException(
                    $"Expiration time {newExpirationUtc} is {timeUntilExpiration.TotalMinutes:F1} minutes in the future, " +
                    $"exceeds max timeout of {AppConfig.MaxInactivityTimeoutMins} minutes");
            }

            // Check if alarm already exists and would fire close to the new expiration time
            // If so, skip rescheduling to reduce unnecessary alarm updates
            var existingAlarm = await _webExtensionsApi.Alarms.Get(AppConfig.SessionManagerAlarmName);
            if (existingAlarm is not null) {
                var existingAlarmTime = DateTimeOffset.FromUnixTimeMilliseconds((long)existingAlarm.ScheduledTime).UtcDateTime;
                var timeDifference = Math.Abs((newExpirationUtc - existingAlarmTime).TotalSeconds);
                if (timeDifference < AppConfig.AlarmRescheduleThresholdSeconds) {
                    _logger.LogDebug(nameof(ScheduleExpirationAlarmAsync) + ": skipped alarm rescheduling (existing alarm at {ExistingTime} is within {Threshold}s of {NewTime})",
                        existingAlarmTime, AppConfig.AlarmRescheduleThresholdSeconds, newExpirationUtc);
                    return;
                }
            }

            // Create or reschedule the alarm
            var whenMs = ((DateTimeOffset)newExpirationUtc).ToUnixTimeMilliseconds();
            await _webExtensionsApi.Alarms.Create(AppConfig.SessionManagerAlarmName, new AlarmInfo {
                When = whenMs
            });
            _logger.LogInformation(nameof(ScheduleExpirationAlarmAsync) + ": alarm scheduled for {Expiration} ({Minutes} min from now)",
                newExpirationUtc, Math.Round(timeUntilExpiration.TotalMinutes, 1));
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(ScheduleExpirationAlarmAsync) + ": Failed to schedule alarm");
            throw new InvalidOperationException("Failed to schedule SessionManager alarm", ex);
        }
    }

    /// <summary>
    /// Creates or ensures the keep-alive periodic alarm is running.
    /// The alarm fires every 30 seconds to prevent the service worker from being terminated by Chrome.
    ///
    /// CRITICAL: The `When` parameter schedules the FIRST fire ~25 seconds from now. Without it,
    /// Chrome only fires the alarm after one full `PeriodInMinutes` interval (~30s). During that
    /// initial gap, Chrome's ~30s idle timeout can terminate the SW, losing the in-memory passcode
    /// and forcing a session lock. This was the root cause of premature locking on fresh install.
    /// </summary>
    private async Task EnsureKeepAliveAlarmAsync() {
        try {
            var existing = await _webExtensionsApi.Alarms.Get(SessionKeepAliveAlarmName);
            if (existing is not null) {
                _logger.LogDebug(nameof(EnsureKeepAliveAlarmAsync) + ": Keep-alive alarm already running");
                return;
            }
            var firstFireMs = ((DateTimeOffset)DateTime.UtcNow.AddSeconds(25)).ToUnixTimeMilliseconds();
            await _webExtensionsApi.Alarms.Create(SessionKeepAliveAlarmName, new AlarmInfo {
                When = firstFireMs,
                PeriodInMinutes = AppConfig.SessionKeepAliveAlarmPeriodMinutes
            });
            _logger.LogInformation(nameof(EnsureKeepAliveAlarmAsync) + ": Keep-alive alarm created (firstFire=25s, period={Period}min)", AppConfig.SessionKeepAliveAlarmPeriodMinutes);
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, nameof(EnsureKeepAliveAlarmAsync) + ": Failed to create keep-alive alarm");
            // Don't throw — keep-alive failure is not fatal
        }
    }

    /// <summary>
    /// Clears the keep-alive alarm. Called when session is locked.
    /// </summary>
    private async Task CancelKeepAliveAlarmAsync() {
        try {
            await _webExtensionsApi.Alarms.Clear(SessionKeepAliveAlarmName);
            _logger.LogInformation(nameof(CancelKeepAliveAlarmAsync) + ": Keep-alive alarm cancelled");
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, nameof(CancelKeepAliveAlarmAsync) + ": Failed to cancel keep-alive alarm");
            // Don't throw — alarm cleanup failure is not fatal
        }
    }

    /// <summary>
    /// Subscribes to storage changes for SessionStateModel and Preferences.
    /// Uses IObserver pattern for immediate reactivity.
    /// </summary>
    private void SubscribeToStorageChanges() {
        // SessionStateModel changes - reschedule alarm when SessionExpirationUtc changes
        _sessionStateObserver = _storageGateway.Subscribe(
            new SessionStateObserver(this),
            StorageArea.Session
        );
        _logger.LogDebug(nameof(SubscribeToStorageChanges) + ": Subscribed to SessionStateModel changes");

        // Preferences changes - immediate SessionExpirationUtc update if unlocked
        _preferencesObserver = _storageGateway.Subscribe(
            new PreferencesObserver(this),
            StorageArea.Local
        );
        _logger.LogDebug(nameof(SubscribeToStorageChanges) + ": Subscribed to Preferences changes");
    }

    /// <summary>
    /// Handles Chrome Alarm events.
    /// Called by BackgroundWorker when SessionManagerAlarm fires.
    /// Alarm is scheduled to fire exactly at session expiration time, so just clear session.
    /// </summary>
    public async Task HandleAlarmAsync(Alarm alarm) {
        if (alarm.Name != AppConfig.SessionManagerAlarmName) {
            _logger.LogError(nameof(HandleAlarmAsync) + ": received unexpected alarm: {Name}", alarm.Name);
            return;
        }

        _logger.LogInformation(nameof(HandleAlarmAsync) + ": alarm fired - session expired, clearing session storage");
        await LockSessionAsync();
    }

    /// <summary>
    /// Handles the keep-alive alarm. No-op: the alarm waking the SW is sufficient.
    /// </summary>
    public void HandleKeepAliveAlarm() {
        _logger.LogDebug(nameof(HandleKeepAliveAlarm) + ": keep-alive alarm fired");
        // No action needed — waking the SW prevents suspension
    }

    /// <summary>
    /// Extends session expiration if unlocked; no-op if session is locked.
    /// Public API called by BackgroundWorker on user activity or preference change.
    /// Throws exception on storage operation failure (fail-fast).
    /// </summary>
    public async Task ExtendIfUnlockedAsync() {
        if (await IsUnlockedAsync()) {
            var prefsRes = await _storageService.GetItem<Preferences>();
            if (prefsRes.IsFailed) {
                throw new InvalidOperationException(
                    $"Failed to get Preferences: {prefsRes.Errors[0].Message}");
            }

            var timeoutMinutes = prefsRes.Value?.InactivityTimeoutMinutes
                ?? AppConfig.DefaultInactivityTimeoutMins;

            var newExpirationUtc = DateTime.UtcNow.AddMinutes(timeoutMinutes);

            // Update SessionStateModel with new expiration time
            var setRes = await _storageService.SetItem<SessionStateModel>(
                new SessionStateModel { SessionExpirationUtc = newExpirationUtc },
                StorageArea.Session
            );
            if (setRes.IsFailed) {
                throw new InvalidOperationException(
                    $"Failed to update SessionStateModel: {setRes.Errors[0].Message}");
            }

            // Schedule one-shot alarm to fire at expiration time
            await ScheduleExpirationAlarmAsync(newExpirationUtc);
        }
        else {
            _logger.LogDebug(nameof(ExtendIfUnlockedAsync) + ": Session not unlocked — nothing to extend");
        }
    }

    /// <summary>
    /// Locks session by clearing KERIA session records (SessionStateModel and KeriaConnectionInfo).
    /// Public API called by BackgroundWorker or when SessionStateModel is cleared.
    /// NOTE: This does NOT clear BwReadyState - BackgroundWorker is still initialized.
    /// Throws exception on storage operation failure (fail-fast).
    /// </summary>
    public async Task LockSessionAsync() {
        _logger.LogInformation(nameof(LockSessionAsync) + ": Locking session (clearing KERIA session records)");
        await ClearKeriaSessionRecordsAsync();
    }

    /// <summary>
    /// Clears KERIA session records from session storage and clears the in-memory passcode.
    /// Does NOT clear BwReadyState - BackgroundWorker initialization state is separate from user session.
    /// Public API for use by App pages that need to clear session state (UnlockPage, MainLayout).
    /// Throws exception on storage operation failure (fail-fast).
    ///
    /// NOTE: Named "KeriaSessionRecords" rather than "Credentials" because in KERI/ACDC domain,
    /// "credential" refers to ACDCs (Authentic Chained Data Containers), not authentication tokens.
    /// </summary>
    public async Task ClearKeriaSessionRecordsAsync() {
        _logger.LogInformation(nameof(ClearKeriaSessionRecordsAsync) + ": Clearing all Session storage");

        // Clear in-memory passcode
        _passcode = null;

        // Cancel keep-alive alarm
        await CancelKeepAliveAlarmAsync();

        // Clear ALL Session storage atomically. This is the authoritative "lock" operation:
        // any new Session record introduced in the future is automatically cleared on lock,
        // ensuring no stale state can survive across an unlock cycle.
        // Session storage is ephemeral by design (cleared on browser close), so bulk clearing
        // is always safe — BwReadyState will self-heal via BackgroundWorker's observer.
        var clearResult = await _storageService.Clear(StorageArea.Session);
        if (clearResult.IsFailed) {
            throw new InvalidOperationException(
                $"Failed to clear Session storage: {clearResult.Errors[0].Message}");
        }

        _logger.LogInformation(nameof(ClearKeriaSessionRecordsAsync) + ": Session storage cleared");
    }

    /// <summary>
    /// Clears ALL session storage when changing KERIA configuration.
    /// This ensures a clean state for the new config.
    ///
    /// Use this when:
    /// - User changes KERIA config on UnlockPage (no-op effect since not authenticated)
    /// - User changes KERIA config on PreferencesPage (locks session)
    ///
    /// Clears: SessionStateModel, KeriaConnectionInfo, BwReadyState, PendingBwAppRequests
    ///
    /// NOTE: BwReadyState is automatically re-established by BackgroundWorker via its
    /// storage observer (BwReadyStateObserver). This self-healing behavior ensures
    /// App initialization doesn't timeout waiting for BwReadyState.
    ///
    /// AppCache will react to storage changes via its observers.
    /// </summary>
    public async Task ClearSessionForConfigChangeAsync() {
        _logger.LogInformation(nameof(ClearSessionForConfigChangeAsync) + ": Clearing ALL session storage for config change");

        // Clear in-memory passcode
        _passcode = null;

        // Cancel keep-alive alarm
        await CancelKeepAliveAlarmAsync();

        var clearResult = await _storageService.Clear(StorageArea.Session);
        if (clearResult.IsFailed) {
            throw new InvalidOperationException(
                $"Failed to clear session storage: {clearResult.Errors[0].Message}");
        }

        _logger.LogInformation(nameof(ClearSessionForConfigChangeAsync) + ": Session storage cleared (BwReadyState will self-heal)");
    }

    /// <summary>
    /// Checks if session is unlocked:
    /// 1. _passcode is set in memory (not null)
    /// 2. SessionStateModel.SessionExpirationUtc is not expired
    /// </summary>
    private async Task<bool> IsUnlockedAsync() {
        // 1. Check passcode is in memory
        if (string.IsNullOrEmpty(_passcode)) {
            _logger.LogInformation(nameof(IsUnlockedAsync) + ": Passcode not in memory — session is locked");
            return false;
        }

        // 2. Check SessionStateModel.SessionExpirationUtc is not expired
        var sessionStateRes = await _storageService.GetItem<SessionStateModel>(StorageArea.Session);
        if (sessionStateRes.IsFailed || sessionStateRes.Value is null) {
            _logger.LogInformation(nameof(IsUnlockedAsync) + ": SessionStateModel not found — session is locked");
            return false;
        }

        var expirationUtc = sessionStateRes.Value.SessionExpirationUtc;
        if (expirationUtc == DateTime.MinValue) {
            _logger.LogDebug(nameof(IsUnlockedAsync) + ": SessionExpirationUtc not set (MinValue) — session is locked");
            return false;
        }

        if (DateTime.UtcNow >= expirationUtc) {
            _logger.LogInformation(nameof(IsUnlockedAsync) + ": Session expired ({Expiration}) — session is locked", expirationUtc);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Restores the extension action icon to the default (unlocked) state.
    /// Uses the original logob icon files specified in manifest.json.
    /// </summary>
    private async Task ClearLockIconAsync() {
        try {
            await _webExtensionsApi.Action.SetBadgeText(new WebExtensions.Net.ActionNs.SetBadgeTextDetails() { Text = "" });
            _logger.LogInformation(nameof(ClearLockIconAsync) + ": Lock icon cleared");
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(ClearLockIconAsync) + ": Failed to clear lock icon");
        }
    }

    /// <summary>
    /// Handles SessionStateModel changes from storage observer.
    /// Reschedules alarm when SessionExpirationUtc changes.
    /// Reacts to current state of SessionStateModel (stateless - works across service worker restarts).
    /// </summary>
    private async Task HandleSessionStateChangeAsync(SessionStateModel? sessionState) {
        if (sessionState is null) {
            // SessionStateModel was removed - session locked
            _logger.LogDebug(nameof(HandleSessionStateChangeAsync) + ": SessionStateModel is null - session locked");
            return;
        }

        var expirationUtc = sessionState.SessionExpirationUtc;

        // Skip if expiration time is invalid (DateTime.MinValue or in the past)
        if (expirationUtc == DateTime.MinValue || expirationUtc <= DateTime.UtcNow) {
            _logger.LogDebug(nameof(HandleSessionStateChangeAsync) + ": SessionStateModel has invalid/expired SessionExpirationUtc {Expiration}, skipping alarm scheduling",
                expirationUtc);
            return;
        }

        _logger.LogDebug(nameof(HandleSessionStateChangeAsync) + ": SessionStateModel changed with SessionExpirationUtc {Expiration}, rescheduling alarm", expirationUtc);

        // Reschedule alarm to fire at expiration time
        try {
            await ScheduleExpirationAlarmAsync(expirationUtc);
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(HandleSessionStateChangeAsync) + ": Failed to reschedule alarm for SessionStateModel change");
            // Don't throw - this is a fire-and-forget observer
        }
    }

    /// <summary>
    /// Handles Preferences changes from storage observer.
    /// When InactivityTimeoutMinutes changes, extends the session with new timeout if unlocked.
    /// </summary>
    private async Task HandlePreferencesChangeAsync(Preferences? preferences) {
        if (preferences is null) {
            _logger.LogWarning(nameof(HandlePreferencesChangeAsync) + ": Preferences changed to null, ignoring");
            return;
        }

        _logger.LogDebug(nameof(HandlePreferencesChangeAsync) + ": Preferences.InactivityTimeoutMinutes changed to {Minutes}, extending session if unlocked",
            preferences.InactivityTimeoutMinutes);

        // Extend session with new timeout value if currently unlocked
        await ExtendIfUnlockedAsync();
    }

    /// <summary>
    /// Observer for SessionStateModel changes.
    /// </summary>
    private sealed class SessionStateObserver(SessionManager sessionManager) : IObserver<SessionStateModel> {
        public void OnNext(SessionStateModel? value) {
            // Fire and forget - errors will be logged and thrown by SessionManager
            _ = sessionManager.HandleSessionStateChangeAsync(value);
        }

        public void OnError(Exception error) {
            sessionManager._logger.LogError(error, nameof(SessionStateObserver) + ": Error in SessionStateModel observer");
        }

        public void OnCompleted() {
            sessionManager._logger.LogDebug(nameof(SessionStateObserver) + ": SessionStateModel observer completed");
        }
    }

    /// <summary>
    /// Observer for Preferences changes.
    /// </summary>
    private sealed class PreferencesObserver(SessionManager sessionManager) : IObserver<Preferences> {
        public void OnNext(Preferences? value) {
            // Fire and forget - errors will be logged and thrown by SessionManager
            _ = sessionManager.HandlePreferencesChangeAsync(value);
        }

        public void OnError(Exception error) {
            sessionManager._logger.LogError(error, nameof(PreferencesObserver) + ": Error in Preferences observer");
        }

        public void OnCompleted() {
            sessionManager._logger.LogDebug(nameof(PreferencesObserver) + ": Preferences observer completed");
        }
    }

    /// <summary>
    /// Disposes storage observers.
    /// </summary>
    public void Dispose() {
        _logger.LogDebug(nameof(Dispose) + ": disposing...");
        _sessionStateObserver?.Dispose();
        _preferencesObserver?.Dispose();
        GC.SuppressFinalize(this);
        _logger.LogDebug(nameof(Dispose) + ": disposed");
    }
}
