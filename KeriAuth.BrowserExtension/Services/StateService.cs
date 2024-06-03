namespace KeriAuth.BrowserExtension.Services;
using KeriAuth.BrowserExtension.Models;
using Stateless;
using static KeriAuth.BrowserExtension.Services.IStateService;


public class StateService : IStateService
{
    private readonly StateMachine<States, Triggers> stateMachine;
    private readonly IStorageService storageService;
    private readonly List<IObserver<States>> stateObservers = [];
    private readonly ILogger<StateService> logger;

    public StateService(IStorageService storageService, ILogger<StateService> logger)
    {
        this.storageService = storageService;
        this.stateMachine = new(States.Uninitialized);
        ConfigureStateMachine();
        this.logger = logger;
    }

    private enum Triggers
    {
        ToInitializing,
        ToUnconfigured,
        ToAuthenticatedDisconnected,
        ToAuthenticatedConnected,
        ToUnauthenticated,
    }

    public States GetState()
    {
        return stateMachine.State;
    }

    public States GetCurrentState()
    {
        return stateMachine.State;
    }

    public async Task Initialize()
    {
        await stateMachine.FireAsync(Triggers.ToInitializing);
    }

    public async Task<bool> IsAuthenticated()
    {
        await Task.Delay(0);
        return stateMachine.IsInState(States.AuthenticatedDisconnected);
    }

    public async Task Authenticate()
    {
        await stateMachine.FireAsync(Triggers.ToAuthenticatedDisconnected);
    }

    public async Task Unauthenticate()
    {
        // "log out"
        await stateMachine.FireAsync(Triggers.ToUnauthenticated);
        // await walletService.CloseWallet();
    }

    public async Task ConfirmConnected()
    {
        await stateMachine.FireAsync(Triggers.ToAuthenticatedConnected);
    }

    IDisposable IObservable<States>.Subscribe(IObserver<States> stateObserver)
    {
        if (!stateObservers.Contains(stateObserver))
        {
            stateObservers.Add(stateObserver);
        }
        return new Unsubscriber(stateObservers, stateObserver);
    }

    public async Task NotifyObservers()
    {
        foreach (var observer in stateObservers)
            observer.OnNext(stateMachine.State);
        await Task.Delay(0); // hack
        return;
    }

    async Task IStateService.Configure()
    {
        await stateMachine.FireAsync(Triggers.ToUnauthenticated);
    }

    async Task IStateService.TimeOut()
    {
        await stateMachine.FireAsync(Triggers.ToUnauthenticated);
    }

    private async Task OnTransitioned(StateMachine<States, Triggers>.Transition t)
    {
        // xxConsole.WriteLine($"StateService transitioned from {t.Source} to {t.Destination} via trigger {t.Trigger}");
        // TODO P3 use JournalService instead, similar to ...
        // await JournalService.Write(new SystemLogEntry(nameof(StateService), SystemLogEntryType.AppStateTransitions));

        // Store the new state, with some exceptions
        if (t.Source != States.Uninitialized && t.Source != States.Initializing)
        {
            var appState = new AppState(t.Destination);
            await storageService.SetItem(appState);
        }
        logger.LogInformation("Transitioned from {oldState} to {newState}", t.Source, t.Destination);
        await NotifyObservers();
    }

    private void ConfigureStateMachine()
    {
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
            .Permit(Triggers.ToInitializing, States.Initializing);

        stateMachine.Configure(States.AuthenticatedDisconnected)
            .OnEntryAsync(async () => await OnEntryAuthenticatedDisconnected())
            .Permit(Triggers.ToInitializing, States.Initializing)
            .Permit(Triggers.ToUnauthenticated, States.Unauthenticated)
            .Permit(Triggers.ToAuthenticatedConnected, States.AuthenticatedConnected);

        stateMachine.Configure(States.AuthenticatedConnected)
            .OnEntryAsync(async () => await OnEntryAuthenticatedConnected());
    }

    private async Task OnEntryUnconfigured()
    {
        await Task.Delay(0);
    }

    private async Task OnEntryAuthenticatedDisconnected()
    {
        await Task.Delay(0);
    }


    private async Task OnEntryAuthenticatedConnected()
    {
        await Task.Delay(0);
    }

    private async Task OnEntryUnauthenticated()
    {
        await Task.Delay(0); // hack
        //var quickLoginResult = await walletService.CheckQuickLogin();
        //if (quickLoginResult.IsSuccess)
        //{
        //    var password = quickLoginResult.Value;
        //    var loadWalletResult = await walletService.LoadWallet(password);
        //    if (loadWalletResult.IsSuccess)
        //    {
        //        await stateMachine.FireAsync(Triggers.ToAuthenticated);
        //        return;
        //    }
        //}
        return;
    }

    private async Task OnEntryInitializing()
    {
        try
        {
            var appStateResult = await storageService.GetItem<AppState>();
            if (appStateResult is not null
                && appStateResult.Value is not null
                && appStateResult.IsSuccess
                && appStateResult.Value.CurrentState != States.Unconfigured)
            {
                await stateMachine.FireAsync(Triggers.ToUnauthenticated);
                return;
            }
            else
            {
                await stateMachine.FireAsync(Triggers.ToUnconfigured);
                return;
            }
        }
        catch (Exception e)
        {
            logger.LogError("Problem with OnEntry RetrievingFromStorage: {e}", e);
        }
        return;
    }

    private class Unsubscriber(List<IObserver<IStateService.States>> observers, IObserver<IStateService.States> observer) : IDisposable
    {
        private readonly List<IObserver<States>> _stateObservers = observers;
        private readonly IObserver<States> _stateObserver = observer;

        public void Dispose()
        {
            if (!(_stateObserver == null)) _stateObservers.Remove(_stateObserver);
        }
    }
}