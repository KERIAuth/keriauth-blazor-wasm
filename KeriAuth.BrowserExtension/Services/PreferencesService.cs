
namespace KeriAuth.BrowserExtension.Services;
using KeriAuth.BrowserExtension.Models;


public class PreferencesService(IStorageService storageService, ILogger<PreferencesService> logger) : IPreferencesService, IObservable<Preferences>, IObserver<Preferences>
{
    private readonly List<IObserver<Preferences>> preferencesObservers = [];
    private readonly IStorageService storageService = storageService;
    // private readonly ILogger<PreferencesService> _logger = new Logger<PreferencesService>(new LoggerFactory());
    private IDisposable? stateSubscription;

    public void Initialize()
    {
        stateSubscription = storageService.Subscribe(this); // TODO consider using parameters for onNext, etc.
    }

    public async Task<Preferences> GetPreferences()
    {
        try
        {
            var preferencesResult = await storageService.GetItem<Preferences>();
            if (preferencesResult is null || preferencesResult.IsFailed)
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
        // See IObserver<Preferences>.OnNext(Preferences value) for pushing updates to overservers
        //
        //foreach (var observer in preferencesObservers)
        //    observer.OnNext(preferences);
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

    private class Unsubscriber(List<IObserver<Preferences>> observers, IObserver<Preferences> observer) : IDisposable // invoked as an observable<Preferences> of ManagePreference or other
    {
        private readonly List<IObserver<Preferences>> _preferencesObservers = observers;
        private readonly IObserver<Preferences> _preferencesObserver = observer;

        public void Dispose()
        {
            if (!(_preferencesObserver == null)) _preferencesObservers.Remove(_preferencesObserver);
        }
    }
}