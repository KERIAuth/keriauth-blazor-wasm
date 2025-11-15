namespace Extension.Services;

using Extension.Models;

public interface IPreferencesService {
    Task SetPreferences(Preferences preferences);

    

}
