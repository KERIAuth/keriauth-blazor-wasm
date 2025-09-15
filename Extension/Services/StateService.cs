namespace Extension.Services;
using Extension.Models;
using FluentResults;
using Stateless;
using WebExtensions.Net;
using static Extension.Services.IStateService;

public class StateService : IStateService {
    private readonly StateMachine<States, Triggers> stateMachine;
    private readonly IStorageService storageService;
    private readonly List<IObserver<States>> stateObservers = [];
    private readonly ILogger<StateService> logger;
    private readonly IWebExtensionsApi webExtensionsApi;

    public StateService(IStorageService storageService, IWebExtensionsApi webExtensionsApi, ILogger<StateService> logger) {
        this.storageService = storageService;
        stateMachine = new(States.Uninitialized);
        ConfigureStateMachine();
        this.logger = logger;
        this.webExtensionsApi = webExtensionsApi;
    }

    private enum Triggers {
        ToInitializing,
        ToUnconfigured,
        ToAuthenticatedDisconnected,
        ToAuthenticatedConnected,
        ToUnauthenticated,
    }

    public States GetState() {
        return stateMachine.State;
    }

    public States GetCurrentState() {
        return stateMachine.State;
    }

    public async Task<Result> Initialize() {
        try {
            await stateMachine.FireAsync(Triggers.ToInitializing);
            return Result.Ok();
        }
        catch (InvalidOperationException e) {
            logger.LogError("Invalid state transition during Initialize: {e}", e.Message);
            return Result.Fail(new StateTransitionError(stateMachine.State.ToString(), "Initializing", e.Message));
        }
    }

    public Result<bool> CheckAuthentication() {
        var isAuthenticated = stateMachine.IsInState(States.AuthenticatedDisconnected) || stateMachine.IsInState(States.AuthenticatedConnected);
        if (isAuthenticated) {
            return Result.Ok(true);
        }
        return Result.Ok(false).WithReason(new AuthenticationError($"Current state is {stateMachine.State}"));
    }

    public async Task<Result> Authenticate(bool isConnected) {
        try {
            if (isConnected) {
                await stateMachine.FireAsync(Triggers.ToAuthenticatedConnected);
            }
            else {
                await stateMachine.FireAsync(Triggers.ToAuthenticatedDisconnected);
            }
            return Result.Ok();
        }
        catch (InvalidOperationException e) {
            var targetState = isConnected ? "AuthenticatedConnected" : "AuthenticatedDisconnected";
            logger.LogError("Invalid state transition during Authenticate: {e}", e.Message);
            return Result.Fail(new StateTransitionError(stateMachine.State.ToString(), targetState, e.Message));
        }
    }

    public async Task<Result> Unauthenticate() {
        // aka "Lock"
        try {
            await stateMachine.FireAsync(Triggers.ToUnauthenticated);
            return Result.Ok();
        }
        catch (InvalidOperationException e) {
            logger.LogError("Invalid state transition during Unauthenticate: {e}", e.Message);
            return Result.Fail(new StateTransitionError(stateMachine.State.ToString(), "Unauthenticated", e.Message));
        }
    }

    public async Task<Result> ConfirmConnected() {
        try {
            await stateMachine.FireAsync(Triggers.ToAuthenticatedConnected);
            return Result.Ok();
        }
        catch (InvalidOperationException e) {
            logger.LogError("Invalid state transition during ConfirmConnected: {e}", e.Message);
            return Result.Fail(new StateTransitionError(stateMachine.State.ToString(), "AuthenticatedConnected", e.Message));
        }
    }

    IDisposable IObservable<States>.Subscribe(IObserver<States> stateObserver) {
        if (!stateObservers.Contains(stateObserver)) {
            stateObservers.Add(stateObserver);
        }
        return new Unsubscriber(stateObservers, stateObserver);
    }

    public async Task NotifyObservers() {
        await Task.Delay(0); // caller does not need to wait
        foreach (var observer in stateObservers) {
            observer.OnNext(stateMachine.State);
        }
        return;
    }

    async Task<Result> IStateService.Configure() {
        try {
            await stateMachine.FireAsync(Triggers.ToUnauthenticated);
            return Result.Ok();
        }
        catch (InvalidOperationException e) {
            logger.LogError("Invalid state transition during Configure: {e}", e.Message);
            return Result.Fail(new StateTransitionError(stateMachine.State.ToString(), "Unauthenticated", e.Message));
        }
    }

    async Task<Result> IStateService.TimeOut() {
        try {
            logger.LogInformation("User selected Locked, or the inactivity timer elapsed, so removing CachedPasscode");
            await webExtensionsApi.Storage.Session.Remove("passcode");
            await stateMachine.FireAsync(Triggers.ToUnauthenticated);
            return Result.Ok();
        }
        catch (Exception e) {
            logger.LogError("Error during TimeOut: {e}", e.Message);
            return Result.Fail(new StateTransitionError(stateMachine.State.ToString(), "Unauthenticated", e.Message));
        }
    }

    private async Task OnTransitioned(StateMachine<States, Triggers>.Transition t) {
        // Store the new state, with some exceptions
        if (t.Source != States.Uninitialized && t.Source != States.Initializing) {
            var appState = new AppState(t.Destination);
            var result = await storageService.SetItem(appState);
            if (result.IsFailed) {
                logger.LogError("Failed to persist state transition: {error}", result.Errors[0].Message);
            }
        }
        logger.LogInformation("Transitioned from {oldState} to {newState}", t.Source, t.Destination);
        await NotifyObservers();
    }

    private void ConfigureStateMachine() {
        stateMachine.OnTransitionCompletedAsync(async (t) => await OnTransitioned(t));

        stateMachine.Configure(States.Uninitialized)
            // Intentionally no OnEntry actions here
            .Permit(Triggers.ToInitializing, States.Initializing);

        stateMachine.Configure(States.Initializing)
            .OnEntryAsync(async () => await OnEntryInitializing())
            .Ignore(Triggers.ToInitializing)
            .Permit(Triggers.ToUnconfigured, States.Unconfigured)
            .Permit(Triggers.ToUnauthenticated, States.Unauthenticated);

        stateMachine.Configure(States.Unconfigured)
            .OnEntryAsync(async () => await OnEntryUnconfigured())
            .Permit(Triggers.ToUnauthenticated, States.Unauthenticated)
            .Permit(Triggers.ToInitializing, States.Initializing);

        stateMachine.Configure(States.Unauthenticated)
            .OnEntryAsync(async () => await OnEntryUnauthenticated())
            .PermitReentry(Triggers.ToUnauthenticated)
            .Permit(Triggers.ToAuthenticatedDisconnected, States.AuthenticatedDisconnected)
            .Permit(Triggers.ToAuthenticatedConnected, States.AuthenticatedConnected)
            .Permit(Triggers.ToInitializing, States.Initializing);

        stateMachine.Configure(States.AuthenticatedDisconnected)
            .OnEntryAsync(async () => await OnEntryAuthenticatedDisconnected())
            .Ignore(Triggers.ToAuthenticatedDisconnected)
            .Permit(Triggers.ToUnauthenticated, States.Unauthenticated)
            .Permit(Triggers.ToAuthenticatedConnected, States.AuthenticatedConnected)
            .Permit(Triggers.ToInitializing, States.Initializing);

        stateMachine.Configure(States.AuthenticatedConnected)
            .OnEntryAsync(async () => await OnEntryAuthenticatedConnected())
            .Ignore(Triggers.ToAuthenticatedConnected)
            .Permit(Triggers.ToUnauthenticated, States.Unauthenticated)
            .Permit(Triggers.ToAuthenticatedDisconnected, States.AuthenticatedDisconnected)
            .Permit(Triggers.ToInitializing, States.Initializing);
    }

    private static async Task OnEntryUnconfigured() {
        await Task.Delay(0);
    }

    private static async Task OnEntryAuthenticatedDisconnected() {
        await Task.Delay(0);
    }

    private static async Task OnEntryAuthenticatedConnected() {
        await Task.Delay(0);
    }

    private static async Task OnEntryUnauthenticated() {
        await Task.Delay(0);
    }

    private async Task OnEntryInitializing() {
        try {
            var appStateResult = await storageService.GetItem<AppState>();
            if (appStateResult is not null
                && appStateResult.Value is not null
                && appStateResult.IsSuccess
                && appStateResult.Value.CurrentState != States.Unconfigured) {
                await stateMachine.FireAsync(Triggers.ToUnauthenticated);
                return;
            }
            else {
                await stateMachine.FireAsync(Triggers.ToUnconfigured);
                return;
            }
        }
        catch (Exception e) {
            logger.LogError("Problem with OnEntry RetrievingFromStorage: {E}", e);
        }
        return;
    }

    private sealed class Unsubscriber(List<IObserver<IStateService.States>> observers, IObserver<IStateService.States> observer) : IDisposable {
        private readonly List<IObserver<States>> _stateObservers = observers;
        private readonly IObserver<States> _stateObserver = observer;

        public void Dispose() {
            if (!(_stateObserver == null)) {
                _stateObservers.Remove(_stateObserver);
            }
        }
    }
}
