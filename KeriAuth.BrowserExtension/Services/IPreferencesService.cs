namespace KeriAuth.BrowserExtension.Services;

using KeriAuth.BrowserExtension.Models;

public interface IPreferencesService : IObservable<Preferences>
{
    Task<Preferences> GetPreferences();

    Task SetPreferences(Preferences preferences);
   
}