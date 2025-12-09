namespace Extension.Services {
    using Extension.Models;
    using Extension.Models.Storage;
    using Extension.Services.SignifyService.Models;

    using Extension.Services.Storage;
    using Extension.Utilities;

    /// <summary>
    /// AppCache provides reactive access to application state stored in browser storage, including some derived properties.
    /// Note: this is not a full state management solution, but a lightweight reactive cache over storage.
    /// Note: remember to call Initialize() after construction to start observing storage changes.
    /// Note: The Change event is raised on any relevant storage change, so this will cause re-renders in dependent components, perhaps more than needed.
    /// The rendering triggering can be optimized by making the comparisons more granular if/as needed or adding more kinds of change events.
    ///
    /// Usage on razor pages:
    ///
    /// For REACTIVE pages (auto-update on storage changes):
    ///   @inject AppCache appCache  // Reactive: subscribes in OnInitializedAsync via SubscribeToAppCache()
    ///   @implements IDisposable
    ///
    ///   In OnInitializedAsync:
    ///     await this.SubscribeToAppCache(appCache);           // Basic: just StateHasChanged
    ///     await this.SubscribeToAppCache(appCache, callback); // With callback after StateHasChanged
    ///
    ///   In Dispose:
    ///     this.UnsubscribeFromAppCache();
    ///
    /// For NON-REACTIVE pages (read-only, no live updates):
    ///   @inject AppCache appCache  // Non-reactive here unless/until SubscribeToAppCache() is added in OnInitializedAsync
    ///
    /// See TestPage for how the IsFoo... reactive properties relate.
    /// See AppCacheComponentExtensions.cs for the SubscribeToAppCache extension method.
    ///
    /// </summary>
    /// <param name="storageService"></param>
    /// <param name="logger"></param>
    public class AppCache(IStorageService storageService, ILogger<AppCache> logger) : IDisposable {
        private readonly IStorageService storageService = storageService;
        private readonly ILogger<AppCache> _logger = logger;
        private bool _isInitialized;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        /// <summary>
        /// Indicates whether AppCache has completed initial fetch of essential storage records.
        /// True after Initialize() has fetched Preferences, OnboardState, and KeriaConnectConfig.
        /// Components can check this to know if AppCache data is available.
        /// </summary>
        // TODO P1: make this a get-only property that checks the essential cached record properties
        public bool IsReady { get; private set; }

        /// <summary>
        /// Indicates whether BackgroundWorker has completed initialization.
        /// Set during EnsureInitializedAsync() after waiting for BwReadyState.
        /// This is a prerequisite for AppCache initialization.
        /// </summary>
        public bool IsBwReady { get; private set; }

        /// <summary>
        /// Default timeout for waiting for BackgroundWorker to become ready.
        /// </summary>
        private const int BwReadyTimeoutMs = 5000;

        /// <summary>
        /// Polling interval when checking for BwReadyState.
        /// </summary>
        private const int BwReadyPollIntervalMs = 200;

        private StorageObserver<Preferences>? preferencesStorageObserver;
        private StorageObserver<OnboardState>? onboardStateStorageObserver;
        private StorageObserver<PasscodeModel>? passcodeModelObserver;
        private StorageObserver<KeriaConnectConfig>? keriaConnectConfigObserver;
        private StorageObserver<KeriaConnectionInfo>? keriaConnectionInfoObserver;

        // Base properties with default values
        public Preferences MyPreferences { get; private set; } = AppConfig.DefaultPreferences;
        public OnboardState MyOnboardState { get; private set; } = new OnboardState();
        public PasscodeModel MyPasscodeModel { get; private set; } = new PasscodeModel() {
            Passcode = "",
            SessionExpirationUtc = DateTime.MinValue // intentionally expired until set
        };
        public static KeriaConnectConfig DefaultKeriaConnectConfig => new KeriaConnectConfig();
        public KeriaConnectConfig MyKeriaConnectConfig { get; private set; } = DefaultKeriaConnectConfig;
        public KeriaConnectionInfo MyKeriaConnectionInfo { get; private set; } = new KeriaConnectionInfo() {
            // SessionExpirationUtc = DateTime.MinValue,
            Config = new KeriaConnectConfig(),
            IdentifiersList = [],
            AgentPrefix = ""
        };

        // Derived properties ("reactive selectors")
        public string SelectedPrefix => MyPreferences.SelectedPrefix;

        private static readonly List<Aid> EmptyAidsList = [];

        public List<Aid> Aids {
            get {
                var identifiersList = MyKeriaConnectionInfo?.IdentifiersList;
                if (identifiersList is null || identifiersList.Count == 0) {
                    return EmptyAidsList;
                }
                return identifiersList.First().Aids ?? EmptyAidsList;
            }
        }

        public bool IsReadyToTransact => IsNotWaiting && IsConnectedToKeria;

        // TODO P2 Could distinguish IsConnectedToKeria (with seeing KeriaConnectionInfo.PasscodeHash exists) versus IsIdentifierFetched
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
            MyKeriaConnectConfig.PasscodeHash == DeterministicHash.ComputeHash(MyPasscodeModel.Passcode);
        public bool IsPasscodeHashSet => MyKeriaConnectConfig.PasscodeHash != 0;
        public bool IsSessionNotExpired => MyPasscodeModel.SessionExpirationUtc > DateTime.UtcNow;
        public bool IsInitialized =>
            IsConfigured;
        public bool IsConfigured =>
            IsKeriaConfigValidated &&
            IsProductOnboarded &&
            MyPreferences.IsStored;
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
            // TODO P2 must confirm it is current version in manifest. Compute in Program or App. Remove from Index.razor
            MyOnboardState.InstallVersionAcknowledged is not null;

        /// <summary>
        /// Waits for AppCache to satisfy all provided assertion functions.
        /// Polls at regular intervals until all assertions return true or timeout occurs.
        /// Used to prevent race conditions where navigation or logic happens before AppCache storage observers fire.
        /// </summary>
        /// <param name="assertions">List of boolean functions to evaluate. All must return true for success.</param>
        /// <param name="maxWaitMs">Maximum time to wait in milliseconds. Default: 5000ms (5 seconds).</param>
        /// <param name="pollIntervalMs">Polling interval in milliseconds. Default: 50ms.</param>
        /// <returns>True if all assertions passed within timeout, false otherwise.</returns>
        public async Task<bool> WaitForAppCache(List<Func<bool>> assertions, int maxWaitMs = 5000, int pollIntervalMs = 50) {
            if (assertions is null || assertions.Count == 0) {
                _logger.LogWarning("WaitForAppCache called with no assertions");
                return true; // No assertions means nothing to wait for
            }

            var elapsedMs = 0;

            while (elapsedMs < maxWaitMs) {
                // Check if all assertions pass
                var allPassed = true;
                foreach (var assertion in assertions) {
                    if (!assertion()) {
                        allPassed = false;
                        break;
                    }
                }

                if (allPassed) {
                    _logger.LogInformation("AppCache assertions all passed after {ElapsedMs}ms", elapsedMs);
                    return true;
                }

                await Task.Delay(pollIntervalMs);
                elapsedMs += pollIntervalMs;
            }

            _logger.LogWarning("AppCache assertions did not all pass after {ElapsedMs}ms timeout", elapsedMs);
            return false;
        }

        public void Dispose() {
            preferencesStorageObserver?.Dispose();
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

                // Perform initial fetch of essential storage records
                // This ensures My* properties have current values before IsReady is set
                _logger.LogInformation("AppCache: Fetching initial storage values");
                await FetchInitialStorageValuesAsync();

                IsReady = true;
                _logger.LogInformation("AppCache initialization complete, IsReady=true");
            }
            finally {
                _initLock.Release();
            }
        }

        /// <summary>
        /// Fetches initial values from storage for all essential records.
        /// Called during Initialize() to populate My* properties before IsReady is set.
        /// This ensures components don't see default values when storage has real data.
        /// </summary>
        private async Task FetchInitialStorageValuesAsync() {
            // Fetch essential records (Local storage - persisted across sessions)
            // These should exist after BackgroundWorker.InitializeStorageDefaultsAsync() runs

            // 1. Preferences
            var prefsResult = await storageService.GetItem<Preferences>(StorageArea.Local);
            if (prefsResult.IsSuccess && prefsResult.Value is not null) {
                MyPreferences = prefsResult.Value;
                _logger.LogDebug("AppCache: Initial fetch - Preferences loaded (IsStored={IsStored})", prefsResult.Value.IsStored);
            }
            else {
                // leaving default (without IsStored = true)
                _logger.LogWarning("AppCache: Initial fetch - Preferences not found or failed, using default");
            }

            // 2. OnboardState
            var onboardResult = await storageService.GetItem<OnboardState>(StorageArea.Local);
            if (onboardResult.IsSuccess && onboardResult.Value is not null) {
                MyOnboardState = onboardResult.Value;
                _logger.LogDebug("AppCache: Initial fetch - OnboardState loaded (IsStored={IsStored}, IsWelcomed={IsWelcomed})",
                    onboardResult.Value.IsStored, onboardResult.Value.IsWelcomed);
            }
            else {
                // leaving default (without IsStored = true)
                _logger.LogWarning("AppCache: Initial fetch - OnboardState not found or failed, using default");
            }

            // 3. KeriaConnectConfig (may not exist on first run - created by ConfigurePage)
            var configResult = await storageService.GetItem<KeriaConnectConfig>(StorageArea.Local);
            if (configResult.IsSuccess && configResult.Value is not null) {
                MyKeriaConnectConfig = configResult.Value;
                _logger.LogDebug("AppCache: Initial fetch - KeriaConnectConfig loaded (IsStored={IsStored}, AdminUrl={AdminUrl})",
                    configResult.Value.IsStored, configResult.Value.AdminUrl ?? "(null)");
            }
            else {
                // leaving default (without IsStored = true)
                _logger.LogDebug("AppCache: Initial fetch - KeriaConnectConfig not found (expected on first run)");
            }

            // Fetch session-scoped records (Session storage - cleared on browser close)
            // These may not exist if session is locked or browser was restarted

            // 4. PasscodeModel (only exists when session is unlocked)
            var passcodeResult = await storageService.GetItem<PasscodeModel>(StorageArea.Session);
            if (passcodeResult.IsSuccess && passcodeResult.Value is not null) {
                MyPasscodeModel = passcodeResult.Value;
                _logger.LogDebug("AppCache: Initial fetch - PasscodeModel loaded (Passcode length={Length})",
                    passcodeResult.Value.Passcode?.Length ?? 0);
            }
            else {
                _logger.LogDebug("AppCache: Initial fetch - PasscodeModel not found (session locked or new)");
            }

            // 5. KeriaConnectionInfo (only exists when connected to KERIA)
            var connectionResult = await storageService.GetItem<KeriaConnectionInfo>(StorageArea.Session);
            if (connectionResult.IsSuccess && connectionResult.Value is not null) {
                MyKeriaConnectionInfo = connectionResult.Value;
                _logger.LogDebug("AppCache: Initial fetch - KeriaConnectionInfo loaded");
            }
            else {
                _logger.LogDebug("AppCache: Initial fetch - KeriaConnectionInfo not found (not connected)");
            }

            _logger.LogInformation("AppCache: Initial fetch complete");
        }

        /// <summary>
        /// Ensures AppCache is initialized and ready before returning.
        /// Call this from components that need AppCache data before proceeding.
        /// This is the preferred entry point for App.razor and other root components.
        ///
        /// This method first waits for BackgroundWorker to complete initialization
        /// (setting BwReadyState.IsInitialized = true in session storage), ensuring
        /// storage defaults are created and expired sessions are cleared before
        /// AppCache reads storage values.
        /// </summary>
        /// <returns>Task that completes when AppCache is ready</returns>
        public async Task EnsureInitializedAsync() {
            if (IsReady) {
                _logger.LogDebug("AppCache: EnsureInitializedAsync - already ready");
                return;
            }

            // Wait for BackgroundWorker to complete initialization first
            // This ensures storage defaults exist and expired sessions are cleared
            IsBwReady = await WaitForBwReadyAsync();
            if (!IsBwReady) {
                _logger.LogWarning("AppCache: BackgroundWorker did not become ready within timeout - proceeding anyway");
                // Continue anyway - will use defaults for missing values
            }

            await Initialize();
        }

        /// <summary>
        /// Waits for BackgroundWorker to set BwReadyState.IsInitialized = true.
        /// Polls session storage at regular intervals until ready or timeout.
        /// </summary>
        /// <returns>True if BackgroundWorker became ready, false if timeout occurred.</returns>
        private async Task<bool> WaitForBwReadyAsync() {
            _logger.LogInformation("AppCache: Waiting for BackgroundWorker initialization (timeout: {TimeoutMs}ms)", BwReadyTimeoutMs);

            var elapsedMs = 0;

            while (elapsedMs < BwReadyTimeoutMs) {
                var result = await storageService.GetItem<BwReadyState>(StorageArea.Session);

                if (result.IsSuccess && result.Value?.IsInitialized == true) {
                    _logger.LogInformation(
                        "AppCache: BackgroundWorker ready after {ElapsedMs}ms (initialized at {InitializedAt})",
                        elapsedMs,
                        result.Value.InitializedAtUtc);
                    return true;
                }

                await Task.Delay(BwReadyPollIntervalMs);
                elapsedMs += BwReadyPollIntervalMs;
            }

            _logger.LogWarning(
                "AppCache: Timeout after {TimeoutMs}ms - BackgroundWorker did not become ready. " +
                "App will proceed but may encounter stale or missing storage data.",
                BwReadyTimeoutMs);
            return false;
        }
    }
}
