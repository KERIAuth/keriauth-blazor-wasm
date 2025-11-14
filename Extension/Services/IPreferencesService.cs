namespace Extension.Services;

using Extension.Models;

public interface IPreferencesService : IObservable<Preferences> {
    Task SetPreferences(Preferences preferences);

    Task Initialize();

}
