namespace Extension.Services {
    using Extension.Models;
    using Extension.Models.Storage;
    using Extension.Services.Storage;

    /// <summary>
    /// AppCache provides reactive access to application state stored in browser storage, including some derived properties.
    /// Note: this is not a full state management solution, but a lightweight reactive cache over storage.
    /// Note: remember to call Initialize() after construction to start observing storage changes.
    /// Note: The Change event is raised on any relevant storage change, so this will cause re-renders in dependent components, perhpas more than needed.
    /// The rendering triggering it can be optomized by making the comparrisons more granular if/as needed or adding more kinds of change events.
    /// </summary>
    /// <param name="storageService"></param>
    /// <param name="logger"></param>
    public class AppCache(IStorageService storageService, ILogger<AppCache> logger) : IDisposable {
        private readonly IStorageService storageService = storageService;
        private readonly ILogger<AppCache> _logger = logger;
        private bool _isInitialized;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        private StorageObserver<Preferences>? preferencesStorageObserver;
        private StorageObserver<SessionExpiration>? inactivityTimeoutCacheModelObserver;
        private StorageObserver<OnboardState>? onboardStateStorageObserver;
        private StorageObserver<PasscodeModel>? passcodeModelObserver;
        private StorageObserver<KeriaConnectConfig>? keriaConnectConfigObserver;

        // Base properties with default values
        public Preferences MyPreferences { get; private set; } = new Preferences();
        public SessionExpiration InactivityTimeoutCacheModel { get; private set; } = new SessionExpiration() { SessionExpirationUtc = DateTime.UtcNow };
        public OnboardState MyOnboardState { get; private set; } = new OnboardState();
        public PasscodeModel MyPasscodeModel { get; private set; } = new PasscodeModel() { Passcode = "" };
        public KeriaConnectConfig MyKeriaConnectConfig { get; private set; } = new KeriaConnectConfig();

        // TODO P3: implement processing requests tracking, when PendingRequests are zero
        private bool IsProcessingRequests { get; set; }

        // Derived properties ("reactive selectors")
        public string SelectedPrefix => MyPreferences.SelectedPrefix;

        // TODO P1: populate from KERI client state, non-static
        public static List<string> XIdentifiers => [];

        public bool IsReady => IsConnected && !IsProcessingRequests;

        // TODO P1 there may be a better way to verify connection, but for now we check that there is at least one identifier
        public bool IsConnected => XIdentifiers.Count > 0 && IsAuthenticated;
        public bool IsAuthenticated => IsSessionReady && IsInitialized;
        public bool IsSessionReady =>
            MyPasscodeModel?.Passcode is not null &&
            MyPasscodeModel.Passcode.Length > 0 &&
            // TODO P2 verify passcode hash matches stored hash
            // Hash(MyPasscodeModel.Passcode) == MyKeriaConnectConfig.PasscodeHash &&
            IsNotExpired;
        public bool IsNotExpired =>
            InactivityTimeoutCacheModel is not null &&
            InactivityTimeoutCacheModel.SessionExpirationUtc > DateTime.UtcNow;
        public bool IsInitialized =>
            IsConfigured;
        public bool IsConfigured =>
            IsKeriaConfigValidated &&
            IsOnboarded &&
            MyPreferences is not null;
        public bool IsKeriaConfigValidated =>
            MyKeriaConnectConfig.AdminUrl is not null &&
            MyKeriaConnectConfig.ValidateConfiguration() is not null &&
            MyKeriaConnectConfig.ValidateConfiguration().IsSuccess &&
            MyKeriaConnectConfig.ValidateConfiguration().Value;
        // TODO P1 implement WasConnected
        // &&
        // MyKeriaConnectConfig.WasConnected;
        public bool IsOnboarded =>
            MyOnboardState.HasAcknowledgedInstall &&
            MyOnboardState.AcknowledgedInstalledVersion is not null &&
            HasAgreedToS &&
            HasAgreedPrivacy;
        public bool HasAgreedToS =>
            MyOnboardState.TosAgreedUtc is not null &&
            MyOnboardState.PrivacyAgreedHash != 0;
        public bool HasAgreedPrivacy =>
            MyOnboardState.PrivacyAgreedUtc is not null &&
            MyOnboardState.PrivacyAgreedHash != 0;

        public void Dispose() {
            preferencesStorageObserver?.Dispose();
            inactivityTimeoutCacheModelObserver?.Dispose();
            onboardStateStorageObserver?.Dispose();
            passcodeModelObserver?.Dispose();
            keriaConnectConfigObserver?.Dispose();
            _initLock?.Dispose();
            GC.SuppressFinalize(this);
        }

        public event Action? Changed;

        public async Task Initialize() {
            // Prevent multiple initializations (singleton service may be accessed from multiple components)
            if (_isInitialized) {
                _logger.LogDebug("AppCache already initialized, skipping");
                return;
            }

            await _initLock.WaitAsync();
            try {
                if (_isInitialized) {
                    return;
                }

                _logger.LogInformation("Initializing AppCache storage observers");

                preferencesStorageObserver = new StorageObserver<Preferences>(
                    storageService,
                    StorageArea.Local,
                    onNext: (value) => {
                        MyPreferences = value;
                        _logger.LogDebug("AppCache updated MyPreferences");
                        Changed?.Invoke();
                    },
                    onError: ex => _logger.LogError(ex, "Error observing preferences storage"),
                    null,
                    _logger
                );
                inactivityTimeoutCacheModelObserver = new StorageObserver<SessionExpiration>(
                    storageService,
                    StorageArea.Session,
                    onNext: (value) => {
                        InactivityTimeoutCacheModel = value;
                        _logger.LogDebug("AppCache updated InactivityTimeoutCacheModel");
                        Changed?.Invoke();
                    },
                    onError: ex => _logger.LogError(ex, "Error observing inactivity timeout cache model storage"),
                    null,
                    _logger
                );
                onboardStateStorageObserver = new StorageObserver<OnboardState>(
                    storageService,
                    StorageArea.Local,
                    onNext: (value) => {
                        MyOnboardState = value;
                        _logger.LogDebug("AppCache updated MyOnboardState");
                        Changed?.Invoke();
                    },
                    onError: ex => _logger.LogError(ex, "Error observing onboard state storage"),
                    null,
                    _logger
                );
                passcodeModelObserver = new StorageObserver<PasscodeModel>(
                    storageService,
                    StorageArea.Session,
                    onNext: (value) => {
                        MyPasscodeModel = value;
                        _logger.LogDebug("AppCache updated MyPasscodeModel: Passcode length={Length}", value.Passcode?.Length ?? 0);
                        Changed?.Invoke();
                    },
                    onError: ex => _logger.LogError(ex, "Error observing user session storage"),
                    null,
                    _logger
                );
                keriaConnectConfigObserver = new StorageObserver<KeriaConnectConfig>(
                    storageService,
                    StorageArea.Local,
                    onNext: (value) => {
                        MyKeriaConnectConfig = value;
                        _logger.LogDebug("AppCache updated MyKeriaConnectConfig");
                        Changed?.Invoke();
                    },
                    onError: ex => _logger.LogError(ex, "Error observing Keria connect config storage"),
                    null,
                    _logger
                );

                _isInitialized = true;
                _logger.LogInformation("AppCache initialization complete");
            }
            finally {
                _initLock.Release();
            }
        }
    }
}
