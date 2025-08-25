namespace KeriAuth.BrowserExtension.Services;

using static KeriAuth.BrowserExtension.Services.IStateService;

public interface IStateService : IObservable<States>
{
    enum States
    {
        Unknown,
        Uninitialized,
        Initializing,
        Unconfigured,
        Unauthenticated,
        AuthenticatedDisconnected,
        AuthenticatedConnected
    }

    States GetState();

    Task Initialize();

    Task Configure();

    Task Authenticate(bool isConnected);

    Task Unauthenticate();

    Task TimeOut();

    Task ConfirmConnected();

    bool IsAuthenticated();
}
