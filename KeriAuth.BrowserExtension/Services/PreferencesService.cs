
namespace KeriAuth.BrowserExtension.Services;
using KeriAuth.BrowserExtension.Models;
using Stateless;
using static KeriAuth.BrowserExtension.Services.IStateService;


public class PreferencesService(IStorageService storageService) : IPreferencesService, IObservable<Preferences>
{
    private readonly List<IObserver<Preferences>> preferencesObservers = [];
    private readonly IStorageService storageService = storageService;
    private readonly ILogger<PreferencesService> _logger = new Logger<PreferencesService>(new LoggerFactory());

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
            else // IsSuccess
            {
                return preferencesResult.Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get preferences");
            return new Preferences();
        }
    }

    public async Task SetPreferences(Preferences preferences)
    {
        await storageService.SetItem<Preferences>(preferences);
        foreach (var observer in preferencesObservers)
            observer.OnNext(preferences);
        return;
    }

    IDisposable IObservable<Preferences>.Subscribe(IObserver<Preferences> preferencesObserver)
    {
        if (!preferencesObservers.Contains(preferencesObserver))
        {
            preferencesObservers.Add(preferencesObserver);
        }
        return new Unsubscriber(preferencesObservers, preferencesObserver);
    }

    private class Unsubscriber(List<IObserver<Preferences>> observers, IObserver<Preferences> observer) : IDisposable
    {
        private readonly List<IObserver<Preferences>> _preferencesObservers = observers;
        private readonly IObserver<Preferences> _preferencesObserver = observer;

        public void Dispose()
        {
            if (!(_preferencesObserver == null)) _preferencesObservers.Remove(_preferencesObserver);
        }
    }
}