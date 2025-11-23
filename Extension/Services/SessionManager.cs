using Extension.Models;
using Extension.Models.Storage;
using Extension.Services.Storage;
using JsBind.Net;
using Microsoft.Extensions.Logging;
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
/// - Creates SessionExpiration immediately when PasscodeModel is set
/// - Updates SessionExpiration immediately when Preferences.InactivityTimeoutMinutes changes
/// - Schedules one-shot alarm to fire at exact expiration time (no polling)
/// - Clears expired sessions on SessionManager startup
/// - Clears session storage when alarm fires or session expires
/// - Validates session is unlocked: passcode hash matches AND session not expired
/// - Provides public API for extending or locking sessions
/// </summary>
public class SessionManager : IDisposable {
    private readonly ILogger<SessionManager> _logger;
    private readonly IStorageService _storageService;
    private readonly WebExtensionsApi _webExtensionsApi;

    // Storage observers - disposed on cleanup
    private IDisposable? _passcodeObserver;
    private IDisposable? _preferencesObserver;
    private IDisposable? _sessionExpirationObserver;

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
    /// Checks if SessionExpiration exists and is expired on SessionManager startup.
    /// Clears session if expired, reschedules alarm if not expired.
    /// Handles cases like: browser suspend/resume, service worker restart, chrome crash.
    /// </summary>
    private async Task CheckAndClearExpiredSessionOnStartupAsync() {
        try {
            var sessionExpRes = await _storageService.GetItem<SessionExpiration>(StorageArea.Session);

            if (sessionExpRes.IsFailed) {
                _logger.LogDebug("No SessionExpiration found on startup - session is locked");
                return;
            }

            var sessionExpiration = sessionExpRes.Value;
            if (sessionExpiration is null) {
                _logger.LogDebug("SessionExpiration is null on startup - session is locked");
                return;
            }

            var expirationUtc = sessionExpiration.SessionExpirationUtc;

            if (DateTime.UtcNow >= expirationUtc) {
                // Session is expired - clear it
                _logger.LogInformation("SessionExpiration expired on startup ({Expiration}), clearing session",
                    expirationUtc);
                await LockSessionAsync();
            }
            else {
                // Session is still valid - reschedule alarm
                _logger.LogInformation("SessionExpiration still valid on startup ({Expiration}), rescheduling alarm",
                    expirationUtc);
                await ScheduleExpirationAlarmAsync(expirationUtc);
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error checking expired session on startup");
            throw new InvalidOperationException("Failed to check expired session on startup", ex);
        }
    }

    /// <summary>
    /// Schedules a one-shot Chrome Alarm to fire at the exact session expiration time.
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
    /// Subscribes to storage changes for PasscodeModel, Preferences, and SessionExpiration.
    /// Uses IObserver pattern for immediate reactivity.
    /// </summary>
    private void SubscribeToStorageChanges() {
        // PasscodeModel changes - immediate SessionExpiration creation
        _passcodeObserver = _storageService.Subscribe(
            new PasscodeObserver(this),
            StorageArea.Session
        );
        _logger.LogDebug("Subscribed to PasscodeModel changes");

        // Preferences changes - immediate SessionExpiration update
        _preferencesObserver = _storageService.Subscribe(
            new PreferencesObserver(this),
            StorageArea.Local
        );
        _logger.LogDebug("Subscribed to Preferences changes");

        // SessionExpiration changes - for logging/debugging
        _sessionExpirationObserver = _storageService.Subscribe(
            new SessionExpirationObserver(this),
            StorageArea.Session
        );
        _logger.LogDebug("Subscribed to SessionExpiration changes");
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
            var prefsRes = await _storageService.GetItem<Preferences>(StorageArea.Local);
            if (prefsRes.IsFailed) {
                throw new InvalidOperationException(
                    $"Failed to get Preferences: {prefsRes.Errors[0].Message}");
            }

            var timeoutMinutes = prefsRes.Value?.InactivityTimeoutMinutes
                ?? AppConfig.DefaultInactivityTimeoutMins;

            var newExpirationUtc = DateTime.UtcNow.AddMinutes(timeoutMinutes);
            var newExpiration = new SessionExpiration {
                SessionExpirationUtc = newExpirationUtc
            };

            var setRes = await _storageService.SetItem(newExpiration, StorageArea.Session);
            if (setRes.IsFailed) {
                throw new InvalidOperationException(
                    $"Failed to set SessionExpiration: {setRes.Errors[0].Message}");
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
    }

    /// <summary>
    /// Checks if session is unlocked by verifying (similar to AppCache.IsSessionUnlocked):
    /// 1. PasscodeModel exists in session storage with non-empty passcode
    /// 2. Passcode hash matches stored hash in KeriaConnectConfig
    /// 3. SessionExpiration exists and is not expired
    /// </summary>
    private async Task<bool> IsUnlockedAsync() {
        // 1. Check PasscodeModel exists and has non-empty passcode
        var passcodeModelRes = await _storageService.GetItem<PasscodeModel>(StorageArea.Session);
        if (passcodeModelRes.IsFailed || passcodeModelRes.Value is null) {
            return false;
        }

        var passcode = passcodeModelRes.Value.Passcode;
        if (string.IsNullOrEmpty(passcode)) {
            return false;
        }

        // 2. Verify passcode hash matches stored hash in KeriaConnectConfig
        var configRes = await _storageService.GetItem<KeriaConnectConfig>(StorageArea.Local);
        if (configRes.IsFailed || configRes.Value is null) {
            return false;
        }

        var storedHash = configRes.Value.PasscodeHash;
        if (storedHash == 0) {
            _logger.LogWarning("PasscodeHash not set in KeriaConnectConfig");
            return false;
        }

        var currentHash = passcode.GetHashCode();
        if (currentHash != storedHash) {
            _logger.LogWarning("Passcode hash mismatch - session not authenticated");
            return false;
        }

        // 3. Check SessionExpiration exists and is not expired
        var sessionExpRes = await _storageService.GetItem<SessionExpiration>(StorageArea.Session);
        if (sessionExpRes.IsFailed || sessionExpRes.Value is null) {
            _logger.LogDebug("SessionExpiration not found - session is locked");
            return false;
        }

        var expirationUtc = sessionExpRes.Value.SessionExpirationUtc;
        if (DateTime.UtcNow >= expirationUtc) {
            _logger.LogInformation("SessionExpiration expired ({Expiration}) - session is locked", expirationUtc);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Handles PasscodeModel changes from storage observer.
    /// Reacts to current state of PasscodeModel (stateless - works across service worker restarts).
    /// </summary>
    private async Task HandlePasscodeChangeAsync(PasscodeModel? passcodeModel) {
        string? passcode = passcodeModel?.Passcode;

        if (!string.IsNullOrEmpty(passcode)) {
            // PasscodeModel exists with passcode - ensure SessionExpiration exists
            // Do NOT call IsUnlockedAsync() here - it requires KeriaConnectConfig to exist,
            // but in ConfigurePage flow, it hasn't been stored yet (race condition).
            // Instead, just ensure SessionExpiration exists based on current Preferences.

            // Check if SessionExpiration already exists
            var sessionExpRes = await _storageService.GetItem<SessionExpiration>(StorageArea.Session);
            if (sessionExpRes.IsSuccess && sessionExpRes.Value is not null) {
                // SessionExpiration already exists, no action needed
                _logger.LogDebug("PasscodeModel exists and SessionExpiration already exists");
                return;
            }

            // SessionExpiration doesn't exist - create it
            _logger.LogInformation("PasscodeModel exists but SessionExpiration missing, creating SessionExpiration");

            try {
                // Get timeout from Preferences
                var prefsRes = await _storageService.GetItem<Preferences>(StorageArea.Local);
                var timeoutMinutes = prefsRes.IsSuccess && prefsRes.Value is not null
                    ? prefsRes.Value.InactivityTimeoutMinutes
                    : AppConfig.DefaultInactivityTimeoutMins;

                var newExpirationUtc = DateTime.UtcNow.AddMinutes(timeoutMinutes);
                var newExpiration = new SessionExpiration {
                    SessionExpirationUtc = newExpirationUtc
                };

                var setRes = await _storageService.SetItem(newExpiration, StorageArea.Session);
                if (setRes.IsFailed) {
                    throw new InvalidOperationException(
                        $"Failed to set SessionExpiration: {setRes.Errors[0].Message}");
                }

                // Schedule one-shot alarm to fire at expiration time
                await ScheduleExpirationAlarmAsync(newExpirationUtc);

                _logger.LogInformation("SessionExpiration created with expiration {Expiration} ({Minutes} min)",
                    newExpirationUtc, Math.Round(timeoutMinutes, 1));
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to create SessionExpiration");
                // Don't throw - this is a fire-and-forget observer
            }
        }
        else {
            // PasscodeModel is null or empty - clear session
            _logger.LogInformation("PasscodeModel is null/empty, clearing session");
            await LockSessionAsync();
        }
    }

    /// <summary>
    /// Handles Preferences changes from storage observer.
    /// If session is unlocked, immediately updates SessionExpiration with new timeout.
    /// </summary>
    private async Task HandlePreferencesChangeAsync(Preferences? preferences) {
        if (preferences is null) {
            _logger.LogWarning("Preferences changed to null, ignoring");
            return;
        }

        // If session is unlocked, immediately update SessionExpiration with new timeout
        if (await IsUnlockedAsync()) {
            _logger.LogInformation("InactivityTimeoutMinutes changed to {Minutes}, updating session immediately",
                preferences.InactivityTimeoutMinutes);
            await ExtendIfUnlockedAsync();
        }
    }

    /// <summary>
    /// Handles SessionExpiration changes from storage observer.
    /// Reschedules alarm when expiration time changes.
    /// </summary>
    private async Task HandleSessionExpirationChangeAsync(SessionExpiration? sessionExpiration) {
        if (sessionExpiration is null) {
            _logger.LogDebug("SessionExpiration cleared - no alarm to schedule");
            return;
        }

        var expirationUtc = sessionExpiration.SessionExpirationUtc;
        _logger.LogDebug("SessionExpiration changed to {Expiration}, rescheduling alarm", expirationUtc);

        // Reschedule alarm to fire at new expiration time
        await ScheduleExpirationAlarmAsync(expirationUtc);
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
    /// Observer for SessionExpiration changes.
    /// Reschedules alarm when expiration time changes.
    /// </summary>
    private sealed class SessionExpirationObserver(SessionManager sessionManager) : IObserver<SessionExpiration> {
        public void OnNext(SessionExpiration? value) {
            // Fire and forget - errors will be logged and thrown by SessionManager
            _ = sessionManager.HandleSessionExpirationChangeAsync(value);
        }

        public void OnError(Exception error) {
            sessionManager._logger.LogError(error, "Error in SessionExpiration observer");
        }

        public void OnCompleted() {
            sessionManager._logger.LogDebug("SessionExpiration observer completed");
        }
    }

    /// <summary>
    /// Disposes storage observers.
    /// </summary>
    public void Dispose() {
        _logger.LogDebug("SessionManager disposing...");
        _passcodeObserver?.Dispose();
        _preferencesObserver?.Dispose();
        _sessionExpirationObserver?.Dispose();
        GC.SuppressFinalize(this);
        _logger.LogDebug("SessionManager disposed");
    }
}
