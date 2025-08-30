namespace Extension.Services;

using Extension.Models;

public interface IPreferencesService : IObservable<Preferences>
{
    Task<Preferences> GetPreferences();

    Task SetPreferences(Preferences preferences);

    Task Initialize();

}