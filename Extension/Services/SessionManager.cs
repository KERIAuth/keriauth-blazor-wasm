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

        _logger.LogInformation("SessionManager initializing...");

        // Start alarm and observers asynchronously
        // Use Task.Run to avoid blocking in browser/Blazor WASM context
        _ = Task.Run(async () => {
            try {
                await StartAsync();
                _logger.LogInformation("SessionManager initialized successfully");
            }
            catch (Exception ex) {
                _logger.LogError(ex, "SessionManager initialization failed");
                throw;
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
                _logger.LogDebug("No PasscodeModel found on startup - session is locked");
                await SetLockIconAsync();
                return;
            }

            var passcodeModel = passcodeModelRes.Value;
            if (passcodeModel is null) {
                _logger.LogDebug("PasscodeModel is null on startup - session is locked");
                await SetLockIconAsync();
                return;
            }

            var expirationUtc = passcodeModel.SessionExpirationUtc;

            // Check for invalid expiration (DateTime.MinValue or past)
            if (expirationUtc == DateTime.MinValue) {
                _logger.LogDebug("PasscodeModel has default expiration (MinValue) on startup - session is locked");
                await SetLockIconAsync();
                return;
            }

            if (DateTime.UtcNow >= expirationUtc) {
                // Session is expired - clear it
                _logger.LogInformation("PasscodeModel expired on startup ({Expiration}), clearing session",
                    expirationUtc);
                await LockSessionAsync();
                return;
            }
            else {
                // Session is still valid - reschedule alarm
                _logger.LogInformation("PasscodeModel still valid on startup ({Expiration}), rescheduling alarm",
                    expirationUtc);
                // TODO P0 tmp await ClearLockIconAsync();
                await SetLockIconAsync();
                await ScheduleExpirationAlarmAsync(expirationUtc);
                return;
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error checking expired session on startup");
            throw new InvalidOperationException("Failed to check expired session on startup", ex);
        }
    }

    /// <summary>
    /// Schedules a named one-shot Chrome Alarm to fire at the exact session expiration time.
    /// Replaces any existing alarm with the same name.
    /// Validates expiration time is in the future and within max timeout range.
    /// </summary>
    private async Task ScheduleExpirationAlarmAsync(DateTime expirationUtc) {
        try {
            var now = DateTime.UtcNow;
            var timeUntilExpiration = expirationUtc - now;

            // Validate expiration is in the future
            if (expirationUtc <= now) {
                throw new ArgumentException(
                    $"Expiration time {expirationUtc} must be in the future (now: {now})");
            }

            // Validate expiration is not too far in the future
            var maxTimeout = TimeSpan.FromMinutes(AppConfig.MaxInactivityTimeoutMins);
            if (timeUntilExpiration > maxTimeout) {
                throw new ArgumentException(
                    $"Expiration time {expirationUtc} is {timeUntilExpiration.TotalMinutes:F1} minutes in the future, " +
                    $"exceeds max timeout of {AppConfig.MaxInactivityTimeoutMins} minutes");
            }

            var whenMs = ((DateTimeOffset)expirationUtc).ToUnixTimeMilliseconds();
            await _webExtensionsApi.Alarms.Create(AppConfig.SessionManagerAlarmName, new AlarmInfo {
                When = whenMs
            });
            _logger.LogInformation("Scheduled SessionManager alarm to fire at {Expiration} (in {Minutes} min)",
                expirationUtc, Math.Round(timeUntilExpiration.TotalMinutes, 1));
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to schedule SessionManager alarm");
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
        _logger.LogDebug("Subscribed to PasscodeModel changes");

        // Preferences changes - immediate SessionExpirationUtc update if unlocked
        _preferencesObserver = _storageService.Subscribe(
            new PreferencesObserver(this),
            StorageArea.Local
        );
        _logger.LogDebug("Subscribed to Preferences changes");
    }

    /// <summary>
    /// Handles Chrome Alarm events.
    /// Called by BackgroundWorker when SessionManagerAlarm fires.
    /// Alarm is scheduled to fire exactly at session expiration time, so just clear session.
    /// </summary>
    public async Task HandleAlarmAsync(Alarm alarm) {
        if (alarm.Name != AppConfig.SessionManagerAlarmName) {  // TODO P1: rename alarm constant
            _logger.LogWarning("SessionManager received unexpected alarm: {Name}", alarm.Name);
            return;
        }

        _logger.LogInformation("SessionManager alarm fired - session expired, clearing session storage");
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

            var prefsRes = await _storageService.GetItem<Preferences>(StorageArea.Local);
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

            var setRes = await _storageService.SetItem(updatedPasscodeModel, StorageArea.Session);
            if (setRes.IsFailed) {
                throw new InvalidOperationException(
                    $"Failed to update PasscodeModel: {setRes.Errors[0].Message}");
            }

            // Schedule one-shot alarm to fire at expiration time
            await ScheduleExpirationAlarmAsync(newExpirationUtc);

            _logger.LogInformation("Session extended to {Expiration} ({Minutes} min)",
                newExpirationUtc, Math.Round(timeoutMinutes, 1));
        }
        else {
            _logger.LogInformation("Session not unlocked, locking session");
            await LockSessionAsync();
        }
    }

    /// <summary>
    /// Locks session by clearing session storage.
    /// Public API called by BackgroundWorker or when PasscodeModel is cleared.
    /// Throws exception on storage operation failure (fail-fast).
    /// </summary>
    public async Task LockSessionAsync() {
        _logger.LogInformation("Locking session (clearing session storage)");
        var clearRes = await _storageService.Clear(StorageArea.Session);
        if (clearRes.IsFailed) {
            throw new InvalidOperationException(
                $"Failed to clear session storage: {clearRes.Errors[0].Message}");
        }
        await SetLockIconAsync();
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
            _logger.LogWarning("PasscodeModel does not exists");
            return false;
        }

        var passcodeModel = passcodeModelRes.Value;
        var passcode = passcodeModel.Passcode;
        if (string.IsNullOrEmpty(passcode)) {
            _logger.LogWarning("Passcode not in PasscodeModel");
            return false;
        }

        // 2. Verify passcode hash matches stored hash in KeriaConnectConfig
        var configRes = await _storageService.GetItem<KeriaConnectConfig>(StorageArea.Local);
        if (configRes.IsFailed || configRes.Value is null) {
            _logger.LogWarning("KeriaConnectConfig not stored");
            return false;
        }

        var storedHash = configRes.Value.PasscodeHash;
        if (storedHash == 0) {
            _logger.LogWarning("PasscodeHash not set in KeriaConnectConfig");
            return false;
        }

        var currentHash = DeterministicHash.ComputeHash(passcode);
        if (currentHash != storedHash) {
            _logger.LogWarning(
                "Passcode hash mismatch - session not authenticated. " +
                "CurrentHash={CurrentHash}, StoredHash={StoredHash}, PasscodeLength={PasscodeLength}",
                currentHash, storedHash, passcode.Length);
            return false;
        }

        // 3. Check PasscodeModel.SessionExpirationUtc is not expired
        var expirationUtc = passcodeModel.SessionExpirationUtc;
        if (expirationUtc == DateTime.MinValue) {
            _logger.LogDebug("PasscodeModel.SessionExpirationUtc not set (MinValue) - session is locked");
            return false;
        }

        if (DateTime.UtcNow >= expirationUtc) {
            _logger.LogInformation("PasscodeModel.SessionExpirationUtc expired ({Expiration}) - session is locked", expirationUtc);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Sets the extension action icon to locked variant using pre-created locked icon files.
    /// Icon files to be created: logoB016-locked.png, logoB032-locked.png, logoB048-locked.png, logoB128-locked.png
    /// </summary>
    private async Task SetLockIconAsync() {
        try {
            await _webExtensionsApi.Action.SetBadgeText(new WebExtensions.Net.ActionNs.SetBadgeTextDetails() { Text = unicodeLockIcon });
            _logger.LogInformation("Lock icon set");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to set lock icon");
        }
    }

    /// <summary>
    /// Restores the extension action icon to the default (unlocked) state.
    /// Uses the original logoB icon files specified in manifest.json.
    /// </summary>
    private async Task ClearLockIconAsync() {
        try {
            await _webExtensionsApi.Action.SetBadgeText(new WebExtensions.Net.ActionNs.SetBadgeTextDetails() { Text = "" });
            _logger.LogInformation("Lock icon cleared");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to clear lock icon");
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
            _logger.LogDebug("PasscodeModel is null/empty - session locked");
            await SetLockIconAsync();
            return;
        }

        var expirationUtc = passcodeModel.SessionExpirationUtc;

        // Skip if expiration time is invalid (DateTime.MinValue or in the past)
        // This can happen during initial PasscodeModel creation or storage clearing
        if (expirationUtc == DateTime.MinValue || expirationUtc <= DateTime.UtcNow) {
            _logger.LogDebug("PasscodeModel has invalid/expired SessionExpirationUtc {Expiration}, skipping alarm scheduling",
                expirationUtc);
            await SetLockIconAsync();
            return;
        }

        _logger.LogDebug("PasscodeModel changed with SessionExpirationUtc {Expiration}, rescheduling alarm", expirationUtc);

        // Session is being unlocked - clear lock icon
        await ClearLockIconAsync();

        // Reschedule alarm to fire at expiration time
        try {
            await ScheduleExpirationAlarmAsync(expirationUtc);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to reschedule alarm for PasscodeModel change");
            // Don't throw - this is a fire-and-forget observer
        }
    }

    /// <summary>
    /// Handles Preferences changes from storage observer.
    /// When InactivityTimeoutMinutes changes, extends the session with new timeout if unlocked.
    /// </summary>
    private async Task HandlePreferencesChangeAsync(Preferences? preferences) {
        if (preferences is null) {
            _logger.LogWarning("Preferences changed to null, ignoring");
            return;
        }

        _logger.LogDebug("Preferences.InactivityTimeoutMinutes changed to {Minutes}, extending session if unlocked",
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
            sessionManager._logger.LogError(error, "Error in PasscodeModel observer");
        }

        public void OnCompleted() {
            sessionManager._logger.LogDebug("PasscodeModel observer completed");
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
            sessionManager._logger.LogError(error, "Error in Preferences observer");
        }

        public void OnCompleted() {
            sessionManager._logger.LogDebug("Preferences observer completed");
        }
    }

    /// <summary>
    /// Disposes storage observers.
    /// </summary>
    public void Dispose() {
        _logger.LogDebug("SessionManager disposing...");
        _passcodeObserver?.Dispose();
        _preferencesObserver?.Dispose();
        GC.SuppressFinalize(this);
        _logger.LogDebug("SessionManager disposed");
    }
}
