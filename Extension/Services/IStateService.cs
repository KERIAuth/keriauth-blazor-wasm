namespace Extension.Services;


public interface IStateService : IObservable<IStateService.States>
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
