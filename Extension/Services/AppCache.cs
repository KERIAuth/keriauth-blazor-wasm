namespace Extension.Services {
    using Extension.Helper;
    using Extension.Models;
    using Extension.Models.Storage;
    using Extension.Services.Storage;

    /// <summary>
    /// AppCache provides reactive access to application state stored in browser storage, including some derived properties.
    /// Note: this is not a full state management solution, but a lightweight reactive cache over storage.
    /// Note: remember to call Initialize() after construction to start observing storage changes.
    /// Note: The Change event is raised on any relevant storage change, so this will cause re-renders in dependent components, perhpas more than needed.
    /// The rendering triggering it can be optomized by making the comparrisons more granular if/as needed or adding more kinds of change events.
    ///
    /// Usage on razor pages:
    /// @inject AppCache appCache   
    /// 
    /// A choice of how an observer will subscribe to changes:
    /// 1) @inherits AppCacheReactiveComponentBase --- for a base component class that automatically subscribes to AppCache changes.
    /// 2) Subscription in OnInitializedAsync... this.SubscribeToAppCache(...) --- for components that cannot inherit from the base class
    ///
    /// See TestPage for how the IsFoo... reactive properties relate
    /// 
    /// </summary>
    /// <param name="storageService"></param>
    /// <param name="logger"></param>
    public class AppCache(IStorageService storageService, ILogger<AppCache> logger) : IDisposable {
        private readonly IStorageService storageService = storageService;
        private readonly ILogger<AppCache> _logger = logger;
        private bool _isInitialized;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        private StorageObserver<Preferences>? preferencesStorageObserver;
        private StorageObserver<SessionExpiration>? sessionExpirationStorageObserver;
        private StorageObserver<OnboardState>? onboardStateStorageObserver;
        private StorageObserver<PasscodeModel>? passcodeModelObserver;
        private StorageObserver<KeriaConnectConfig>? keriaConnectConfigObserver;
        private StorageObserver<KeriaConnectionInfo>? keriaConnectionInfoObserver;

        // Base properties with default values
        public Preferences MyPreferences { get; private set; } = new Preferences();
        public SessionExpiration MySessionExpiration { get; private set; } = new SessionExpiration() { SessionExpirationUtc = DateTime.MinValue }; // intentionally expired until set
        public OnboardState MyOnboardState { get; private set; } = new OnboardState();
        public PasscodeModel MyPasscodeModel { get; private set; } = new PasscodeModel() { Passcode = "" };
        public static KeriaConnectConfig DefaultKeriaConnectConfig => new KeriaConnectConfig();
        public KeriaConnectConfig MyKeriaConnectConfig { get; private set; } = DefaultKeriaConnectConfig;
        public KeriaConnectionInfo MyKeriaConnectionInfo { get; private set; } = new KeriaConnectionInfo() {
            SessionExpirationUtc = DateTime.MinValue,
            Config = new KeriaConnectConfig(),
            IdentifiersList = [],
            AgentPrefix = ""
        };

        // Derived properties ("reactive selectors")
        public string SelectedPrefix => MyPreferences.SelectedPrefix;

        // TODO P1: populate from KERI client state, non-static
        public static List<string> XIdentifiers => [];

        public bool IsReadyToTransact => IsNotWaiting && IsConnectedToKeria;

        // TODO P1 there may be a better way to verify connection, but for now we check that there is at least one identifier
        public bool IsConnectedToKeria => IsIdentifierFetched && IsAuthenticated;

        // TODO P2 fix squirelly structure of IdentifiersList and Aids. Not intuitive here, with structured as aids, end, start, total.
        // plus, name "IsIdentifierFetched" is more like IsConnectedToKeria, but that conflicts with the above property name that's perhaps unn
        public bool IsIdentifierFetched =>
            MyKeriaConnectConfig.AgentAidPrefix is not null &&
            MyKeriaConnectionInfo.IdentifiersList?.FirstOrDefault()?.Aids.Count > 0;
        public bool IsAuthenticated => IsSessionUnlocked && IsInitialized;

        public bool IsNotWaiting =>
            IsNotWaitingOnKeria &&
            IsNotWaitingOnUser &&
            IsNotWaitingOnPendingRequests &&
            IsBwNotWaitingOnApp &&
            IsAppNotWaitingOnBw;
        // TODO P2: implement real user NotWaiting... tracking. Example: outstanding Request from CS.  Dialog open. OOBI in progress, etc.
        public bool IsNotWaitingOnKeria { get; private set; }
        public bool IsNotWaitingOnUser { get; private set; }
        public bool IsBwNotWaitingOnApp { get; private set; }
        public bool IsAppNotWaitingOnBw { get; private set; }
        public bool IsNotWaitingOnPendingRequests { get; private set; }
        public bool IsSessionUnlocked =>
            IsPasscodeHashSet &&
            IsSessionPasscodeLocallyValid &&
            IsSessionNotExpired &&
            IsSessionPasscodeSet;
        public bool IsSessionPasscodeSet =>
            MyPasscodeModel.Passcode is not null &&
            MyPasscodeModel.Passcode.Length == 21;
        public bool IsSessionPasscodeLocallyValid =>
            MyPasscodeModel.Passcode is not null &&
            MyPasscodeModel.Passcode.Length == 21 &&
            MyKeriaConnectConfig.PasscodeHash == MyPasscodeModel.Passcode.GetHashCode();
        public bool IsPasscodeHashSet => MyKeriaConnectConfig.PasscodeHash != 0;
        public bool IsSessionNotExpired => MySessionExpiration.SessionExpirationUtc > DateTime.UtcNow;
        public bool IsInitialized =>
            IsConfigured;
        public bool IsConfigured =>
            IsKeriaConfigValidated &&
            IsProductOnboarded &&
            MyPreferences is not null;
        // TODO P3 add other aspects of KeriaConfig validation as needed.  See also ValidateConfiguration() in KeriaConnectConfig.cs
        public bool IsKeriaConfigValidated =>
            !string.IsNullOrEmpty(MyKeriaConnectConfig.AdminUrl) &&
            !string.IsNullOrWhiteSpace(MyKeriaConnectConfig.ClientAidPrefix) &&
            !string.IsNullOrWhiteSpace(MyKeriaConnectConfig.AgentAidPrefix) &&
            !(MyKeriaConnectConfig.PasscodeHash == 0) &&
            !string.IsNullOrWhiteSpace(MyKeriaConnectConfig.AdminUrl);

        private static bool IsValidHttpUri(string? uriString) {
            return Uri.TryCreate(uriString, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        public bool IsKeriaInitialConnectSuccess => !string.IsNullOrEmpty(MyKeriaConnectConfig.ClientAidPrefix) && IsKeriaConfigValidated && IsProductOnboarded;
        public bool IsProductOnboarded =>
            MyOnboardState.IsWelcomed &&
            MyOnboardState.InstallVersionAcknowledged is not null &&
            IsTosAgreed &&
            IsPrivacyAgreed;
        public bool IsTosAgreed =>
            IsTosAgreedUtc &&
            IsTosHashExpected;
        public bool IsTosAgreedUtc => MyOnboardState.TosAgreedUtc is not null;
        public bool IsTosHashExpected => MyOnboardState.TosAgreedHash == AppConfig.ExpectedTermsDigest;
        public bool IsPrivacyAgreed =>
            IsPrivacyAgreedUtc &&
            IsPrivacyHashExpected;
        public bool IsPrivacyAgreedUtc => MyOnboardState.PrivacyAgreedUtc is not null;
        public bool IsPrivacyHashExpected => MyOnboardState.PrivacyAgreedHash == AppConfig.ExpectedPrivacyDigest;
        public bool IsInstallAcknowledged =>
            MyOnboardState.IsWelcomed &&
            IsInstalledVersionAcknowledged;
        public bool IsInstalledVersionAcknowledged =>
            // TODO P1 must confirm it is current version in manifest. Compute in Program or App. Remove from Index.razor
            MyOnboardState.InstallVersionAcknowledged is not null;
        public void Dispose() {
            preferencesStorageObserver?.Dispose();
            sessionExpirationStorageObserver?.Dispose();
            onboardStateStorageObserver?.Dispose();
            passcodeModelObserver?.Dispose();
            keriaConnectConfigObserver?.Dispose();
            keriaConnectionInfoObserver?.Dispose();
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
                        _logger.LogInformation("AppCache updated MyPreferences");
                        Changed?.Invoke();
                    },
                    onError: ex => _logger.LogError(ex, "Error observing preferences storage"),
                    null,
                    _logger
                );
                sessionExpirationStorageObserver = new StorageObserver<SessionExpiration>(
                    storageService,
                    StorageArea.Session,
                    onNext: (value) => {
                        MySessionExpiration = value;
                        _logger.LogInformation("AppCache updated InactivityTimeoutCacheModel");
                        Changed?.Invoke();
                    },
                    onError: ex => _logger.LogError(ex, "Error observing inactivity timeout cache model storage"),
                    onCompleted: () => {
                        _logger.LogInformation("InactivityTimeoutCacheModel observation completed");
                    },
                    _logger
                );
                onboardStateStorageObserver = new StorageObserver<OnboardState>(
                    storageService,
                    StorageArea.Local,
                    onNext: (value) => {
                        MyOnboardState = value;
                        _logger.LogInformation("AppCache updated MyOnboardState");
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
                        _logger.LogInformation("AppCache updated MyPasscodeModel: Passcode length={Length}", value.Passcode?.Length ?? 0);
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
                        _logger.LogInformation("AppCache updated MyKeriaConnectConfig");
                        Changed?.Invoke();
                    },
                    onError: ex => _logger.LogError(ex, "Error observing Keria connect config storage"),
                    null,
                    _logger
                );
                keriaConnectionInfoObserver = new StorageObserver<KeriaConnectionInfo>(
                    storageService,
                    StorageArea.Session,
                    onNext: (value) => {
                        MyKeriaConnectionInfo = value;
                        _logger.LogInformation("AppCache updated MyKeriaConnectionInfo");
                        Changed?.Invoke();
                    },
                    onError: ex => _logger.LogError(ex, "Error observing Keria connection info storage"),
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
