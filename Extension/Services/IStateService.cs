using FluentResults;

namespace Extension.Services;

public interface IStateService : IObservable<IStateService.States> {
    enum States {
        Unknown,
        Uninitialized,
        Initializing,
        Unconfigured,
        Unauthenticated,
        AuthenticatedDisconnected,
        AuthenticatedConnected
    }

    States GetState();

    Task<Result> Initialize();

    Task<Result> Configure();

    Task<Result> Authenticate(bool isConnected);

    Task<Result> Unauthenticate();

    Task<Result> TimeOut();

    Task<Result> ConfirmConnected();

    Result<bool> CheckAuthentication();
}
