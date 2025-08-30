
namespace Extension.Services;

using JsBind.Net;
using Extension.Models;
using WebExtensions.Net;

public class PreferencesService(IStorageService storageService, ILogger<PreferencesService> logger, IJsRuntimeAdapter jsRuntimeAdapter) : IPreferencesService, IObservable<Preferences>, IObserver<Preferences>, IDisposable {
    private readonly List<IObserver<Preferences>> preferencesObservers = [];
    private readonly IStorageService storageService = storageService;
    // private readonly ILogger<PreferencesService> _logger = new Logger<PreferencesService>(new LoggerFactory());
    private IDisposable? stateSubscription;
    private WebExtensionsApi? webExtensionsApi;
    private bool _disposed;

    public async Task Initialize()
    {
        stateSubscription = storageService.Subscribe(this);
        webExtensionsApi = new WebExtensionsApi(jsRuntimeAdapter);
        await Task.Delay(0);
    }

    public async Task<Preferences> GetPreferences()
    {
        try
        {
            var preferencesResult = await storageService.GetItem<Preferences>();
            if (preferencesResult is null || preferencesResult.IsFailed || preferencesResult.Value is null)
            {
                // If preferences don't yet exist in storage, return the defaults
                return new Preferences();
            }
            else
            {
                return preferencesResult.Value;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get preferences");
            return new Preferences();
        }
    }

    void IObserver<Preferences>.OnCompleted() // invoked as an observer<Preferences> of StorageService
    {
        throw new NotImplementedException();
    }

    void IObserver<Preferences>.OnError(Exception error) // invoked as an observer<Preferences> of StorageService
    {
        throw new NotImplementedException();
    }

    void IObserver<Preferences>.OnNext(Preferences value) // invoked as an observer<Preferences> of StorageService
    {
        logger.LogInformation("Preferences updated: {value}", value.ToString());
        foreach (var observer in preferencesObservers)
        {
            observer.OnNext(value);
        }
    }

    public async Task SetPreferences(Preferences preferences)
    {
        await storageService.SetItem<Preferences>(preferences);

        // since we also use InactivityTimeoutMinutes very frequently, we also want this in fast session storage
        webExtensionsApi = new WebExtensionsApi(jsRuntimeAdapter);
        var data = new Dictionary<string, object?> { { "inactivityTimeoutMinutes", preferences.InactivityTimeoutMinutes } };
        await webExtensionsApi.Storage.Session.Set(data);
        // and reset the current inactivityTimeout to immediately pick up the new value, which might be shorter than the currently active one
        await webExtensionsApi.Runtime.SendMessage(new { action = "resetInactivityTimer" });

        return;
    }

    IDisposable IObservable<Preferences>.Subscribe(IObserver<Preferences> preferencesObserver) // invoked as an observable<Preferences> of ManagePreference or other // TODO consider using parameters for onNext, etc.
    {
        if (!preferencesObservers.Contains(preferencesObserver))
        {
            preferencesObservers.Add(preferencesObserver);
        }
        return new Unsubscriber(preferencesObservers, preferencesObserver);
    }

    private sealed class Unsubscriber(List<IObserver<Preferences>> observers, IObserver<Preferences> observer) : IDisposable // invoked as an observable<Preferences> of ManagePreference or other
    {
        private readonly List<IObserver<Preferences>> _preferencesObservers = observers;
        private readonly IObserver<Preferences> _preferencesObserver = observer;

        public void Dispose()
        {
            if (!(_preferencesObserver == null)) {
                _preferencesObservers.Remove(_preferencesObserver);
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            // Dispose managed resources.
            stateSubscription?.Dispose();
            stateSubscription = null;
            preferencesObservers.Clear();

            // If webExtensionsApi implements IDisposable, dispose it as well.
            if (webExtensionsApi is IDisposable disposableApi)
            {
                disposableApi.Dispose();
                webExtensionsApi = null;
            }
        }

        // Note: No unmanaged resources to free.
        _disposed = true;
    }
}
