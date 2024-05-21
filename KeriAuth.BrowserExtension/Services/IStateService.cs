namespace KeriAuth.BrowserExtension.Services;

using KeriAuth.BrowserExtension.Models;
using static KeriAuth.BrowserExtension.Services.IStateService;

public interface IStateService : IObservable<States>
{
    public enum States
    {
        Unknown,
        Uninitialized,
        Initializing,
        Unconfigured,
        Unauthenticated,
        Authenticated
    }

    public States GetState();

    public Task Initialize();

    public Task Configure();

    public Task Authenticate();

    public Task Unauthenticate();

    public Task TimeOut();

    public Task<bool> IsAuthenticated();
}