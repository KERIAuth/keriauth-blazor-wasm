
namespace Extension.Services;

using Extension.Models;
using Extension.Models.Storage;
using Extension.Services.Storage;
using WebExtensions.Net;

public class PreferencesService(IStorageService storageService, ILogger<PreferencesService> logger) : IPreferencesService, IDisposable {
    private readonly List<IObserver<Preferences>> preferencesObservers = [];
    private readonly IStorageService storageService = storageService;
    // private readonly ILogger<PreferencesService> _logger = new Logger<PreferencesService>(new LoggerFactory());
    private StorageObserver<Preferences>? storageObserver;
    private WebExtensionsApi? webExtensionsApi;
    private bool _disposed;

    /*
    public async Task Initialize() {
        // Use new StorageObserver to monitor Preferences changes in Local storage
        storageObserver = new StorageObserver<Preferences>(
            storageService,
            StorageArea.Local,
            HandlePreferencesChanged,
            ex => logger.LogError(ex, "Error observing preferences storage"),
            null,
            logger
        );
        webExtensionsApi = new WebExtensionsApi(jsRuntimeAdapter);
        await Task.Delay(0);
    }
    

    private void HandlePreferencesChanged(Preferences value) {
        logger.LogInformation("Preferences updated: {value}", value.ToString());
        foreach (var observer in preferencesObservers) {
            observer.OnNext(value);
        }
    }
    */

    public async Task SetPreferences(Preferences preferences) {
        logger.LogInformation("SetPreferences...");
        await storageService.SetItem<Preferences>(preferences);

        // Cache the session expiration time in session storage for fast access
        // Calculate when session should expire based on inactivity timeout
        var sessionExpirationUtc = DateTime.UtcNow.AddMinutes(preferences.InactivityTimeoutMinutes);
        var cacheResult = await storageService.SetItem(
            new SessionExpiration { SessionExpirationUtc = sessionExpirationUtc },
            StorageArea.Session
        );
        if (cacheResult.IsFailed) {
            logger.LogWarning("Failed to cache session expiration time: {Errors}",
                string.Join(", ", cacheResult.Errors));
        }

        // Reset the current inactivityTimeout to immediately pick up the new value, which might be shorter than the currently active one
        // TODO P1 await webExtensionsApi!.Runtime.SendMessage(new { action = "resetInactivityTimer" });
        logger.LogInformation("SetPreferences done");
        return;
    }

    /*
    IDisposable IObservable<Preferences>.Subscribe(IObserver<Preferences> preferencesObserver) // invoked as an observable<Preferences> of ManagePreference or other // TODO consider using parameters for onNext, etc.
    {
        if (!preferencesObservers.Contains(preferencesObserver)) {
            preferencesObservers.Add(preferencesObserver);
        }
        return new Unsubscriber(preferencesObservers, preferencesObserver);
    }

    private sealed class Unsubscriber(List<IObserver<Preferences>> observers, IObserver<Preferences> observer) : IDisposable // invoked as an observable<Preferences> of ManagePreference or other
    {
        private readonly List<IObserver<Preferences>> _preferencesObservers = observers;
        private readonly IObserver<Preferences> _preferencesObserver = observer;

        public void Dispose() {
            if (!(_preferencesObserver == null)) {
                _preferencesObservers.Remove(_preferencesObserver);
            }
        }
    }
    */

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) {
        if (_disposed) {
            return;
        }

        if (disposing) {
            // Dispose managed resources.
            storageObserver?.Dispose();
            storageObserver = null;
            preferencesObservers.Clear();

            // If webExtensionsApi implements IDisposable, dispose it as well.
            if (webExtensionsApi is IDisposable disposableApi) {
                disposableApi.Dispose();
                webExtensionsApi = null;
            }
        }

        // Note: No unmanaged resources to free.
        _disposed = true;
    }
}
