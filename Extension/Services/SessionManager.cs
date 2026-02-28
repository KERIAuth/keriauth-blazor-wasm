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
/// 1. Storage observers for immediate reactivity to PasscodeModel and Preferences changes
/// 2. One-shot Chrome Alarm scheduled to fire exactly at session expiration time
/// 3. Startup check to handle browser suspend/resume and service worker restarts
///
/// Responsibilities:
/// - Updates PasscodeModel.SessionExpirationUtc immediately when PasscodeModel is set
/// - Updates PasscodeModel.SessionExpirationUtc immediately when Preferences.InactivityTimeoutMinutes changes
/// - Schedules one-shot alarm to fire at exact expiration time (no polling)
/// - Clears expired sessions on SessionManager startup
/// - Clears session storage when alarm fires or session expires
/// - Validates session is unlocked: passcode hash matches AND session not expired
/// - Provides public API for extending or locking sessions
///
/// ATOMIC STORAGE: PasscodeModel contains both Passcode and SessionExpirationUtc fields,
/// ensuring reactive listeners never see intermediate state where passcode exists without expiration.
/// </summary>
public class SessionManager : IDisposable {
    private readonly ILogger<SessionManager> _logger;
    private readonly IStorageService _storageService;
    private readonly WebExtensionsApi _webExtensionsApi;
    private const string unicodeLockIcon = "\U0001F512"; // Unicode lock icon 🔒

    // Storage observers - disposed on cleanup
    private IDisposable? _passcodeObserver;
    private IDisposable? _preferencesObserver;

    /// <summary>
    /// Constructor starts SessionManager initialization asynchronously.
    /// Initialization runs in background to avoid blocking in browser/Blazor WASM context.
    /// Errors during initialization are logged but do not prevent construction.
    /// </summary>
    public SessionManager(
        ILogger<SessionManager> logger,
        IStorageService storageService,
        IJsRuntimeAdapter jsRuntimeAdapter) {
        _logger = logger;
        _storageService = storageService;
        _webExtensionsApi = new WebExtensionsApi(jsRuntimeAdapter);

        _logger.LogInformation(nameof(SessionManager) + ": initializing...");

        // Start alarm and observers asynchronously
        // Use Task.Run to avoid blocking in browser/Blazor WASM context
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

    /// <summary>
    /// Starts SessionManager: checks for expired session, subscribes to storage changes.
    /// Throws exception on failure (fail-fast).
    /// </summary>
    private async Task StartAsync() {
        // 1. Check for expired session on startup (handles browser suspend/resume, service worker restart)
        await CheckAndClearExpiredSessionOnStartupAsync();

        // 2. Subscribe to storage changes for immediate reactivity
        SubscribeToStorageChanges();
    }

    /// <summary>
    /// Checks if PasscodeModel exists and is expired on SessionManager startup.
    /// Clears session if expired, reschedules alarm if not expired.
    /// Handles cases like: browser suspend/resume, service worker restart, chrome crash.
    /// </summary>
    private async Task CheckAndClearExpiredSessionOnStartupAsync() {
        try {
            var passcodeModelRes = await _storageService.GetItem<PasscodeModel>(StorageArea.Session);

            if (passcodeModelRes.IsFailed) {
                _logger.LogDebug(nameof(CheckAndClearExpiredSessionOnStartupAsync) + ": No PasscodeModel found on startup - session is locked");
                await SetLockIconAsync();
                return;
            }

            var passcodeModel = passcodeModelRes.Value;
            if (passcodeModel is null) {
                _logger.LogDebug(nameof(CheckAndClearExpiredSessionOnStartupAsync) + ": PasscodeModel is null on startup - session is locked");
                await SetLockIconAsync();
                return;
            }

            var expirationUtc = passcodeModel.SessionExpirationUtc;

            // Check for invalid expiration (DateTime.MinValue or past)
            if (expirationUtc == DateTime.MinValue) {
                _logger.LogDebug(nameof(CheckAndClearExpiredSessionOnStartupAsync) + ": PasscodeModel has default expiration (MinValue) on startup - session is locked");
                await SetLockIconAsync();
                return;
            }

            if (DateTime.UtcNow >= expirationUtc) {
                // Session is expired - clear it
                _logger.LogInformation(nameof(CheckAndClearExpiredSessionOnStartupAsync) + ": PasscodeModel expired on startup ({Expiration}), clearing session",
                    expirationUtc);
                await LockSessionAsync();
                return;
            }
            else {
                // Session is still valid - reschedule alarm
                _logger.LogInformation(nameof(CheckAndClearExpiredSessionOnStartupAsync) + ": PasscodeModel still valid on startup ({Expiration}), rescheduling alarm",
                    expirationUtc);
                await SetLockIconAsync();
                await ScheduleExpirationAlarmAsync(expirationUtc);
                return;
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(CheckAndClearExpiredSessionOnStartupAsync) + ": Error checking expired session on startup");
            throw new InvalidOperationException("Failed to check expired session on startup", ex);
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
                if (timeDifference < 15) { // TODO P2 put in AppConfig
                    _logger.LogDebug(nameof(ScheduleExpirationAlarmAsync) + ": skipped alarm rescheduling (existing alarm at {ExistingTime} is within 15s of {NewTime})",
                        existingAlarmTime, newExpirationUtc);
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
    /// Subscribes to storage changes for PasscodeModel and Preferences.
    /// Uses IObserver pattern for immediate reactivity.
    /// </summary>
    private void SubscribeToStorageChanges() {
        // PasscodeModel changes - reschedule alarm when SessionExpirationUtc changes
        _passcodeObserver = _storageService.Subscribe(
            new PasscodeObserver(this),
            StorageArea.Session
        );
        _logger.LogDebug(nameof(SubscribeToStorageChanges) + ": Subscribed to PasscodeModel changes");

        // Preferences changes - immediate SessionExpirationUtc update if unlocked
        _preferencesObserver = _storageService.Subscribe(
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
    /// Extends session expiration if unlocked, otherwise locks session.
    /// Public API called by BackgroundWorker on user activity.
    /// Throws exception on storage operation failure (fail-fast).
    /// </summary>
    public async Task ExtendIfUnlockedAsync() {
        if (await IsUnlockedAsync()) {
            // Get current PasscodeModel
            var passcodeModelRes = await _storageService.GetItem<PasscodeModel>(StorageArea.Session);
            if (passcodeModelRes.IsFailed || passcodeModelRes.Value is null) {
                throw new InvalidOperationException("PasscodeModel not found when extending session");
            }

            var prefsRes = await _storageService.GetItem<Preferences>();
            if (prefsRes.IsFailed) {
                throw new InvalidOperationException(
                    $"Failed to get Preferences: {prefsRes.Errors[0].Message}");
            }

            var timeoutMinutes = prefsRes.Value?.InactivityTimeoutMinutes
                ?? AppConfig.DefaultInactivityTimeoutMins;

            var newExpirationUtc = DateTime.UtcNow.AddMinutes(timeoutMinutes);

            // Update PasscodeModel with new expiration time (atomic update)
            var updatedPasscodeModel = passcodeModelRes.Value with {
                SessionExpirationUtc = newExpirationUtc
            };

            var setRes = await _storageService.SetItem<PasscodeModel>(updatedPasscodeModel, StorageArea.Session);
            if (setRes.IsFailed) {
                throw new InvalidOperationException(
                    $"Failed to update PasscodeModel: {setRes.Errors[0].Message}");
            }

            // Schedule one-shot alarm to fire at expiration time
            await ScheduleExpirationAlarmAsync(newExpirationUtc);


        }
        else {
            _logger.LogInformation(nameof(ExtendIfUnlockedAsync) + ": Session not unlocked, locking session");
            await LockSessionAsync();
        }
    }

    /// <summary>
    /// Locks session by clearing KERIA session records (PasscodeModel and KeriaConnectionInfo).
    /// Public API called by BackgroundWorker or when PasscodeModel is cleared.
    /// NOTE: This does NOT clear BwReadyState - BackgroundWorker is still initialized.
    /// Throws exception on storage operation failure (fail-fast).
    /// </summary>
    public async Task LockSessionAsync() {
        _logger.LogInformation(nameof(LockSessionAsync) + ": Locking session (clearing KERIA session records)");
        await ClearKeriaSessionRecordsAsync();
        await SetLockIconAsync();
    }

    /// <summary>
    /// Clears KERIA session records (PasscodeModel and KeriaConnectionInfo) from session storage.
    /// Does NOT clear BwReadyState - BackgroundWorker initialization state is separate from user session.
    /// Public API for use by App pages that need to clear session state (UnlockPage, MainLayout).
    /// Throws exception on storage operation failure (fail-fast).
    ///
    /// NOTE: Named "KeriaSessionRecords" rather than "Credentials" because in KERI/ACDC domain,
    /// "credential" refers to ACDCs (Authentic Chained Data Containers), not authentication tokens.
    /// </summary>
    public async Task ClearKeriaSessionRecordsAsync() {
        _logger.LogInformation(nameof(ClearKeriaSessionRecordsAsync) + ": Removing KERIA session records");

        var removePasscodeRes = await _storageService.RemoveItem<PasscodeModel>(StorageArea.Session);
        if (removePasscodeRes.IsFailed) {
            throw new InvalidOperationException(
                $"Failed to remove PasscodeModel: {removePasscodeRes.Errors[0].Message}");
        }

        var removeConnectionRes = await _storageService.RemoveItem<KeriaConnectionInfo>(StorageArea.Session);
        if (removeConnectionRes.IsFailed) {
            throw new InvalidOperationException(
                $"Failed to remove KeriaConnectionInfo: {removeConnectionRes.Errors[0].Message}");
        }

        var removeCredentialsRes = await _storageService.RemoveItem<CachedCredentials>(StorageArea.Session);
        if (removeCredentialsRes.IsFailed) {
            throw new InvalidOperationException(
                $"Failed to remove CachedCredentials: {removeCredentialsRes.Errors[0].Message}");
        }

        var removeNotificationsRes = await _storageService.RemoveItem<Notifications>(StorageArea.Session);
        if (removeNotificationsRes.IsFailed) {
            throw new InvalidOperationException(
                $"Failed to remove Notifications: {removeNotificationsRes.Errors[0].Message}");
        }

        _logger.LogInformation(nameof(ClearKeriaSessionRecordsAsync) + ": KERIA session records cleared");
    }

    /// <summary>
    /// Clears ALL session storage when changing KERIA configuration.
    /// This ensures a clean state for the new config.
    ///
    /// Use this when:
    /// - User changes KERIA config on UnlockPage (no-op effect since not authenticated)
    /// - User changes KERIA config on PreferencesPage (locks session)
    ///
    /// Clears: PasscodeModel, KeriaConnectionInfo, BwReadyState, PendingBwAppRequests
    ///
    /// NOTE: BwReadyState is automatically re-established by BackgroundWorker via its
    /// storage observer (BwReadyStateObserver). This self-healing behavior ensures
    /// App initialization doesn't timeout waiting for BwReadyState.
    ///
    /// AppCache will react to storage changes via its observers.
    /// </summary>
    public async Task ClearSessionForConfigChangeAsync() {
        _logger.LogInformation(nameof(ClearSessionForConfigChangeAsync) + ": Clearing ALL session storage for config change");

        var clearResult = await _storageService.Clear(StorageArea.Session);
        if (clearResult.IsFailed) {
            throw new InvalidOperationException(
                $"Failed to clear session storage: {clearResult.Errors[0].Message}");
        }

        await SetLockIconAsync();
        _logger.LogInformation(nameof(ClearSessionForConfigChangeAsync) + ": Session storage cleared (BwReadyState will self-heal)");
    }

    /// <summary>
    /// Checks if session is unlocked by verifying (similar to AppCache.IsSessionUnlocked):
    /// 1. PasscodeModel exists in session storage with non-empty passcode
    /// 2. Passcode hash matches stored hash in KeriaConnectConfig
    /// 3. PasscodeModel.SessionExpirationUtc is not expired
    /// </summary>
    private async Task<bool> IsUnlockedAsync() {
        // 1. Check PasscodeModel exists and has non-empty passcode
        var passcodeModelRes = await _storageService.GetItem<PasscodeModel>(StorageArea.Session);
        if (passcodeModelRes.IsFailed || passcodeModelRes.Value is null) {
            _logger.LogInformation(nameof(IsUnlockedAsync) + ": PasscodeModel does not exists");
            return false;
        }

        var passcodeModel = passcodeModelRes.Value;
        var passcode = passcodeModel.Passcode;
        if (string.IsNullOrEmpty(passcode)) {
            _logger.LogWarning(nameof(IsUnlockedAsync) + ": Passcode not in PasscodeModel");
            return false;
        }

        // 2. Verify passcode hash matches stored hash in KeriaConnectConfig
        // First get preferences to find the selected config digest
        var prefsRes = await _storageService.GetItem<Preferences>();
        if (prefsRes.IsFailed || prefsRes.Value is null) {
            _logger.LogWarning(nameof(IsUnlockedAsync) + ": Preferences not stored");
            return false;
        }

        var selectedDigest = prefsRes.Value.KeriaPreference.SelectedKeriaConnectionDigest;
        if (string.IsNullOrEmpty(selectedDigest)) {
            _logger.LogWarning(nameof(IsUnlockedAsync) + ": SelectedKeriaConnectionDigest not set in Preferences");
            return false;
        }

        // Get the KeriaConnectConfigs dictionary
        var configsRes = await _storageService.GetItem<KeriaConnectConfigs>();
        if (configsRes.IsFailed || configsRes.Value is null || !configsRes.Value.IsStored) {
            _logger.LogWarning(nameof(IsUnlockedAsync) + ": KeriaConnectConfigs not stored");
            return false;
        }

        // Look up the selected config by digest
        if (!configsRes.Value.Configs.TryGetValue(selectedDigest, out var selectedConfig)) {
            _logger.LogWarning(nameof(IsUnlockedAsync) + ": Selected KeriaConnectConfig not found for digest {Digest}", selectedDigest);
            return false;
        }

        var storedHash = selectedConfig.PasscodeHash;
        if (storedHash == 0) {
            _logger.LogWarning(nameof(IsUnlockedAsync) + ": PasscodeHash not set in selected KeriaConnectConfig");
            return false;
        }

        var currentHash = DeterministicHash.ComputeHash(passcode);
        if (currentHash != storedHash) {
            _logger.LogWarning(
                nameof(IsUnlockedAsync) + ": Passcode hash mismatch - session not authenticated. " +
                "CurrentHash={CurrentHash}, StoredHash={StoredHash}, PasscodeLength={PasscodeLength}",
                currentHash, storedHash, passcode.Length);
            return false;
        }

        // 3. Check PasscodeModel.SessionExpirationUtc is not expired
        var expirationUtc = passcodeModel.SessionExpirationUtc;
        if (expirationUtc == DateTime.MinValue) {
            _logger.LogDebug(nameof(IsUnlockedAsync) + ": PasscodeModel.SessionExpirationUtc not set (MinValue) - session is locked");
            return false;
        }

        if (DateTime.UtcNow >= expirationUtc) {
            _logger.LogInformation(nameof(IsUnlockedAsync) + ": PasscodeModel.SessionExpirationUtc expired ({Expiration}) - session is locked", expirationUtc);
            return false;
        }

        return true;
    }

    /// <summary>
    /// TODO P2: TBD Sets the extension action icon to locked variant using pre-created locked icon files.
    /// Icon files to be created: logob016-locked.png, logob032-locked.png, logob048-locked.png, logob128-locked.png
    /// </summary>
    private async Task SetLockIconAsync() {
        _logger.LogInformation(nameof(SetLockIconAsync) + ": Not setting lock icon for now");
        /*
        try {
            await _webExtensionsApi.Action.SetBadgeText(new WebExtensions.Net.ActionNs.SetBadgeTextDetails() { Text = unicodeLockIcon });
            _logger.LogInformation("Lock icon set");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to set lock icon");
        }
        */
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
    /// Handles PasscodeModel changes from storage observer.
    /// Reschedules alarm when SessionExpirationUtc changes.
    /// Reacts to current state of PasscodeModel (stateless - works across service worker restarts).
    /// </summary>
    private async Task HandlePasscodeChangeAsync(PasscodeModel? passcodeModel) {
        if (passcodeModel is null || string.IsNullOrEmpty(passcodeModel.Passcode)) {
            // PasscodeModel is null or empty - session locked
            _logger.LogDebug(nameof(HandlePasscodeChangeAsync) + ": PasscodeModel is null/empty - session locked");
            await SetLockIconAsync();
            return;
        }

        var expirationUtc = passcodeModel.SessionExpirationUtc;

        // Skip if expiration time is invalid (DateTime.MinValue or in the past)
        // This can happen during initial PasscodeModel creation or storage clearing
        if (expirationUtc == DateTime.MinValue || expirationUtc <= DateTime.UtcNow) {
            _logger.LogDebug(nameof(HandlePasscodeChangeAsync) + ": PasscodeModel has invalid/expired SessionExpirationUtc {Expiration}, skipping alarm scheduling",
                expirationUtc);
            await SetLockIconAsync();
            return;
        }

        _logger.LogDebug(nameof(HandlePasscodeChangeAsync) + ": PasscodeModel changed with SessionExpirationUtc {Expiration}, rescheduling alarm", expirationUtc);

        // Session is being unlocked - clear lock icon
        await ClearLockIconAsync();

        // Reschedule alarm to fire at expiration time
        try {
            await ScheduleExpirationAlarmAsync(expirationUtc);
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(HandlePasscodeChangeAsync) + ": Failed to reschedule alarm for PasscodeModel change");
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
    /// Observer for PasscodeModel changes.
    /// </summary>
    private sealed class PasscodeObserver(SessionManager sessionManager) : IObserver<PasscodeModel> {
        public void OnNext(PasscodeModel? value) {
            // Fire and forget - errors will be logged and thrown by SessionManager
            _ = sessionManager.HandlePasscodeChangeAsync(value);
        }

        public void OnError(Exception error) {
            sessionManager._logger.LogError(error, nameof(PasscodeObserver) + ": Error in PasscodeModel observer");
        }

        public void OnCompleted() {
            sessionManager._logger.LogDebug(nameof(PasscodeObserver) + ": PasscodeModel observer completed");
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
        _passcodeObserver?.Dispose();
        _preferencesObserver?.Dispose();
        GC.SuppressFinalize(this);
        _logger.LogDebug(nameof(Dispose) + ": disposed");
    }
}
