namespace KeriAuth.BrowserExtension.Services; 
using KeriAuth.BrowserExtension.Models;
using Stateless;
using static KeriAuth.BrowserExtension.Services.IStateService;


public class StateService : IStateService
{
    private readonly StateMachine<States, Triggers> _stateMachine;
    private readonly IStorageService _storageService;
    private readonly IWalletService _walletService;
    private readonly List<IObserver<States>> stateObservers = [];
    private readonly ILogger<StateService> _logger;

    public StateService(IStorageService storageService, IWalletService walletService, ILogger<StateService> logger)
    {
        _storageService = storageService;
        _stateMachine = new(States.Uninitialized);
        ConfigureStateMachine();
        _walletService = walletService;
        _logger = logger;
    }

    private enum Triggers
    {
        ToInitializing,
        ToUnconfigured,
        ToAuthenticated,
        ToUnauthenticated,
    }

    public States GetState()
    {
        return _stateMachine.State;
    }

    public States GetCurrentState()
    {
        return _stateMachine.State;
    }

    public async Task Initialize()
    {
        await _stateMachine.FireAsync(Triggers.ToInitializing);
    }

    public async Task<bool> IsAuthenticated()
    {
        await Task.Delay(0);
        return _stateMachine.IsInState(States.Authenticated);
    }

    public async Task Authenticate()
    {
        await _stateMachine.FireAsync(Triggers.ToAuthenticated);
    }

    public async Task Unauthenticate()
    {
        // "log out"
        await _stateMachine.FireAsync(Triggers.ToUnauthenticated);
        // await _walletService.CloseWallet();
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
            observer.OnNext(_stateMachine.State);
        await Task.Delay(0); // hack
        return;
    }

    async Task IStateService.Configure()
    {
        await _stateMachine.FireAsync(Triggers.ToUnauthenticated);
    }

    async Task IStateService.TimeOut()
    {
        await _stateMachine.FireAsync(Triggers.ToUnauthenticated);
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
            await _storageService.SetItem(appState);
        }
        _logger.LogInformation("Transitioned from {oldState} to {newState}", t.Source, t.Destination);
        await NotifyObservers();
    }

    private void ConfigureStateMachine()
    {
        _stateMachine.OnTransitionCompletedAsync(async (t) => await OnTransitioned(t));

        _stateMachine.Configure(States.Uninitialized)
            // Intentionally no OnEntry actions here
            .Permit(Triggers.ToInitializing, States.Initializing);

        _stateMachine.Configure(States.Initializing)
            .OnEntryAsync(async () => await OnEntryInitializing())
            .Ignore(Triggers.ToInitializing)
            .Permit(Triggers.ToUnconfigured, States.Unconfigured)
            .Permit(Triggers.ToUnauthenticated, States.Unauthenticated);

        _stateMachine.Configure(States.Unconfigured)
            .OnEntryAsync(async () => await OnEntryUnconfigured())
            .Permit(Triggers.ToUnauthenticated, States.Unauthenticated)
            .Permit(Triggers.ToInitializing, States.Initializing);

        _stateMachine.Configure(States.Unauthenticated)
            .OnEntryAsync(async () => await OnEntryUnauthenticated())
            .PermitReentry(Triggers.ToUnauthenticated)
            .Permit(Triggers.ToAuthenticated, States.Authenticated)
            .Permit(Triggers.ToInitializing, States.Initializing);

        _stateMachine.Configure(States.Authenticated)
            .OnEntryAsync(async () => await OnEntryAuthenticated())
            .Permit(Triggers.ToInitializing, States.Initializing)
            .Permit(Triggers.ToUnauthenticated, States.Unauthenticated);
    }

    private static async Task OnEntryUnconfigured()
    {
        await Task.Delay(0);
    }

    private static async Task OnEntryAuthenticated()
    {
        await Task.Delay(0);
    }

    private static async Task OnEntryUnauthenticated()
    {
        await Task.Delay(0); // hack
        //var quickLoginResult = await _walletService.CheckQuickLogin();
        //if (quickLoginResult.IsSuccess)
        //{
        //    var password = quickLoginResult.Value;
        //    var loadWalletResult = await _walletService.LoadWallet(password);
        //    if (loadWalletResult.IsSuccess)
        //    {
        //        await _stateMachine.FireAsync(Triggers.ToAuthenticated);
        //        return;
        //    }
        //}
        return;
    }

    private async Task OnEntryInitializing()
    {
        try
        {
            // WalletService must be initialized by now
            var isWalletExists = await _walletService.CheckIfWalletExists();
            if (!isWalletExists)
            {
                await _stateMachine.FireAsync(Triggers.ToUnconfigured);
                return;
            }
            else
            {
                var appStateResult = await _storageService.GetItem<AppState>();
                if (appStateResult is not null
                    && appStateResult.Value is not null
                    && appStateResult.IsSuccess
                    && appStateResult.Value.CurrentState != States.Unconfigured)
                {
                    await _stateMachine.FireAsync(Triggers.ToUnauthenticated);
                    return;
                }
                else
                {
                    if (isWalletExists)
                    {
                        await _stateMachine.FireAsync(Triggers.ToUnauthenticated);
                        return;
                    }
                    else
                    {
                        await _stateMachine.FireAsync(Triggers.ToUnconfigured);
                        return;
                    }
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogError("Problem with OnEntry RetrievingFromStorage: {e}", e);
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