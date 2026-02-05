namespace Extension.Services {
    using System.Text.Json;
    using Extension.Models;
    using Extension.Models.Storage;
    using Extension.Services.SignifyService.Models;

    using Extension.Services.Storage;
    using Extension.Utilities;
    using WebExtensions.Net;



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
    /// <param name="webExtensionsApi"></param>
    public class AppCache(IStorageService storageService, ILogger<AppCache> logger, IWebExtensionsApi webExtensionsApi) : IDisposable {
        private readonly IStorageService storageService = storageService;
        private readonly ILogger<AppCache> _logger = logger;
        private bool _isInitialized;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private readonly IWebExtensionsApi webExtensionsApi = webExtensionsApi;

        /// <summary>
        /// Indicates whether AppCache has completed initial fetch of essential storage records.
        /// True after Initialize() has fetched Preferences, OnboardState, and KeriaConnectConfig.
        /// Components can check this to know if AppCache data is available.
        /// </summary>
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
        // TODO P2: move to AppConfig
        private const int BwReadyTimeoutMs = 5000;

        /// <summary>
        /// Polling interval when checking for BwReadyState.
        /// </summary>
        // TODO P2: move to AppConfig
        private const int BwReadyPollIntervalMs = 200;

        private StorageObserver<Preferences>? preferencesStorageObserver;
        private StorageObserver<OnboardState>? onboardStateStorageObserver;
        private StorageObserver<PasscodeModel>? passcodeModelObserver;
        private StorageObserver<KeriaConnectConfig>? keriaConnectConfigObserver;
        private StorageObserver<KeriaConnectConfigs>? keriaConnectConfigsObserver;
        private StorageObserver<KeriaConnectionInfo>? keriaConnectionInfoObserver;
        private StorageObserver<PendingBwAppRequests>? pendingBwAppRequestsObserver;

        // Base properties with default values
        public Preferences MyPreferences { get; private set; } = AppConfig.DefaultPreferences;
        public OnboardState MyOnboardState { get; private set; } = new OnboardState();
        public PasscodeModel MyPasscodeModel { get; private set; } = new PasscodeModel() {
            Passcode = "",
            SessionExpirationUtc = DateTime.MinValue // intentionally expired until set
        };
        public static KeriaConnectConfig DefaultKeriaConnectConfig => new KeriaConnectConfig();

        /// <summary>
        /// Collection of all KERIA configurations keyed by their KeriaConnectionDigest.
        /// </summary>
        public KeriaConnectConfigs MyKeriaConnectConfigs { get; private set; } = new KeriaConnectConfigs();

        /// <summary>
        /// The currently selected KERIA configuration based on preferences.
        /// Falls back to DefaultKeriaConnectConfig if no selection or selection not found.
        /// </summary>
        public KeriaConnectConfig MyKeriaConnectConfig => GetSelectedKeriaConnectConfig() ?? DefaultKeriaConnectConfig;
        public KeriaConnectionInfo MyKeriaConnectionInfo { get; private set; } = new KeriaConnectionInfo() {
            KeriaConnectionDigest = "",
            IdentifiersList = []
        };

        /// <summary>
        /// Gets the KeriaConnectConfig for the current session by looking up MyKeriaConnectionInfo.KeriaConnectionDigest.
        /// Returns null if no session or digest not found in configs.
        /// </summary>
        public KeriaConnectConfig? SessionKeriaConnectConfig =>
            GetConfigByDigest(MyKeriaConnectionInfo.KeriaConnectionDigest);

        /// <summary>
        /// Alias of the KERIA connection for the current session.
        /// Derived from SessionKeriaConnectConfig lookup.
        /// </summary>
        public string? SessionKeriaAlias => SessionKeriaConnectConfig?.Alias;

        /// <summary>
        /// AdminUrl of the KERIA connection for the current session.
        /// Derived from SessionKeriaConnectConfig lookup.
        /// </summary>
        public string? SessionKeriaAdminUrl => SessionKeriaConnectConfig?.AdminUrl;

        /// <summary>
        /// ClientAidPrefix of the KERIA connection for the current session.
        /// Derived from SessionKeriaConnectConfig lookup.
        /// </summary>
        public string? SessionKeriaClientAidPrefix => SessionKeriaConnectConfig?.ClientAidPrefix;

        /// <summary>
        /// AgentAidPrefix of the KERIA connection for the current session.
        /// Derived from SessionKeriaConnectConfig lookup.
        /// </summary>
        public string? SessionKeriaAgentAidPrefix => SessionKeriaConnectConfig?.AgentAidPrefix;

        /// <summary>
        /// PasscodeHash of the KERIA connection for the current session.
        /// Derived from SessionKeriaConnectConfig lookup.
        /// </summary>
        public int SessionKeriaPasscodeHash => SessionKeriaConnectConfig?.PasscodeHash ?? 0;

        /// <summary>
        /// Pending requests from BackgroundWorker awaiting App processing.
        /// Direction: BackgroundWorker → App
        /// Components can check HasPendingBwAppRequests or NextPendingBwAppRequest to react to incoming requests.
        /// </summary>
        public PendingBwAppRequests MyPendingBwAppRequests { get; private set; } = PendingBwAppRequests.Empty;

        // Derived properties ("reactive selectors")
        /// <summary>
        /// Gets the selected AID prefix from the current KERIA configuration.
        /// Each config stores its own SelectedPrefix, so switching configs automatically
        /// switches to that config's selected identifier.
        /// Returns null if not connected to KERIA (use IsConnectedToKeria to check first).
        /// </summary>
        public string? SelectedPrefix => IsConnectedToKeria ? MyKeriaConnectConfig.SelectedPrefix : null;

        /// <summary>
        /// Gets a KeriaConnectConfig by its digest from the configs collection.
        /// </summary>
        /// <param name="digest">The KeriaConnectionDigest to look up.</param>
        /// <returns>The config if found, null otherwise.</returns>
        public KeriaConnectConfig? GetConfigByDigest(string? digest) {
            if (string.IsNullOrEmpty(digest)) return null;
            return MyKeriaConnectConfigs.Configs.TryGetValue(digest, out var config) ? config : null;
        }

        /// <summary>
        /// Gets the currently selected KeriaConnectConfig based on preferences.
        /// </summary>
        /// <returns>The selected config if found, null otherwise.</returns>
        public KeriaConnectConfig? GetSelectedKeriaConnectConfig() {
            return GetConfigByDigest(MyPreferences.KeriaPreference.SelectedKeriaConnectionDigest);
        }

        /// <summary>
        /// Gets all available KeriaConnectConfigs as a list.
        /// </summary>
        /// <returns>List of all configured KERIA connections.</returns>
        public List<KeriaConnectConfig> GetAvailableKeriaConfigs() {
            return MyKeriaConnectConfigs.Configs.Values.ToList();
        }

        /// <summary>
        /// Clears the KERIA connection info synchronously.
        /// Call this after clearing KeriaConnectionInfo from storage to ensure AppCache
        /// reflects the change immediately (rather than waiting for async storage observer).
        /// This ensures IsIdentifierFetched returns false for proper routing through ConnectingPage.
        /// </summary>
        public void ClearKeriaConnectionInfo() {
            MyKeriaConnectionInfo = new KeriaConnectionInfo() {
                KeriaConnectionDigest = "",
                IdentifiersList = []
            };
            _logger.LogInformation("AppCache: Cleared KeriaConnectionInfo synchronously");
            Changed?.Invoke();
        }

        /// <summary>
        /// Validates that the session's KeriaConnectionDigest matches the selected preference.
        /// Throws InvalidOperationException if there's a mismatch (indicates session/preference desync).
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when session digest doesn't match preference.</exception>
        public void ValidateSessionDigestMatchesPreference() {
            var sessionDigest = MyKeriaConnectionInfo.KeriaConnectionDigest;
            var preferenceDigest = MyPreferences.KeriaPreference.SelectedKeriaConnectionDigest;

            // Empty session digest is valid (no active session)
            if (string.IsNullOrEmpty(sessionDigest)) {
                return;
            }

            // If there's an active session, its digest must match the preference
            if (sessionDigest != preferenceDigest) {
                _logger.LogError("Session KeriaConnectionDigest '{SessionDigest}' does not match preference '{PreferenceDigest}'",
                    sessionDigest, preferenceDigest);
                throw new InvalidOperationException(
                    $"Session KeriaConnectionDigest '{sessionDigest}' does not match preference '{preferenceDigest}'");
            }
        }

        /// <summary>
        /// Validates that the SelectedPrefix exists among the fetched identifiers.
        /// Only validates when the KeriaConnectionInfo digest matches the selected preference digest,
        /// ensuring we're comparing against identifiers from the correct config.
        /// Logs a warning (does not throw) if SelectedPrefix is set but not found among identifiers.
        /// </summary>
        /// <returns>True if validation passed or was skipped (no mismatch), false if SelectedPrefix not found.</returns>
        public bool ValidateSelectedPrefixAmongIdentifiers() {
            // Only validate when connection info digest matches the selected preference
            // During config switches, KeriaConnectionInfo may be stale (from previous config)
            var connectionDigest = MyKeriaConnectionInfo?.KeriaConnectionDigest;
            var preferenceDigest = MyPreferences.KeriaPreference.SelectedKeriaConnectionDigest;

            if (string.IsNullOrEmpty(connectionDigest)) {
                // No connection info yet, skip validation
                return true;
            }

            if (connectionDigest != preferenceDigest) {
                // Connection info is from a different config than the selected one
                // This is expected during config switches - skip validation
                _logger.LogDebug(
                    "Skipping SelectedPrefix validation: connection digest '{ConnectionDigest}' != preference digest '{PreferenceDigest}'",
                    connectionDigest, preferenceDigest);
                return true;
            }

            // Get SelectedPrefix directly from the current config (not the property which checks IsConnectedToKeria)
            var selectedPrefix = MyKeriaConnectConfig.SelectedPrefix;

            // Empty SelectedPrefix is valid (not set yet)
            if (string.IsNullOrEmpty(selectedPrefix)) {
                return true;
            }

            // If no identifiers are fetched, skip validation (will be validated when identifiers arrive)
            var identifiersList = MyKeriaConnectionInfo?.IdentifiersList;
            if (identifiersList is null || identifiersList.Count == 0) {
                return true;
            }

            // Check if SelectedPrefix is among the identifier prefixes
            var allPrefixes = identifiersList
                .SelectMany(ids => ids.Aids)
                .Select(aid => aid.Prefix)
                .ToHashSet();

            if (!allPrefixes.Contains(selectedPrefix)) {
                _logger.LogWarning(
                    "SelectedPrefix '{SelectedPrefix}' is not among the fetched identifiers for config '{Digest}'. " +
                    "Available prefixes: {Prefixes}. This may indicate a config/data inconsistency.",
                    selectedPrefix, connectionDigest, string.Join(", ", allPrefixes));
                return false;
            }

            return true;
        }

        /// <summary>
        /// True if there are pending BW→App requests awaiting processing.
        /// </summary>
        public bool HasPendingBwAppRequests => !MyPendingBwAppRequests.IsEmpty;

        /// <summary>
        /// The next pending BW→App request to process (oldest first), or null if none.
        /// </summary>
        public PendingBwAppRequest? NextPendingBwAppRequest => MyPendingBwAppRequests.NextRequest;

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
        public bool ShowedGettingStarted => MyOnboardState.ShowedGettingStarted;
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
            IsCurrentTosAndPrivacyAgreed;

        public bool IsCurrentTosAndPrivacyAgreed =>
            IsTosHashExpected &&
            IsPrivacyHashExpected;

        public bool IsCurrentTosAgreed =>
            IsTosAgreedUtc &&
            IsTosHashExpected;
        public bool IsTosAgreedUtc => MyOnboardState.TosAgreedUtc is not null;
        public bool IsTosHashExpected => MyOnboardState.TosAgreedHash == AppConfig.ExpectedTermsDigest && AppConfig.ExpectedTermsDigest == App.CurrentTermsDigest;
        public bool IsCurrentPrivacyAgreed =>
            IsPrivacyAgreedUtc &&
            IsPrivacyHashExpected;
        public bool IsPrivacyAgreedUtc => MyOnboardState.PrivacyAgreedUtc is not null;
        public bool IsPrivacyHashExpected =>  MyOnboardState.PrivacyAgreedHash == AppConfig.ExpectedPrivacyDigest && AppConfig.ExpectedPrivacyDigest == App.CurrentPrivacyDigest;
        public bool IsTermsAndPrivacyAgreed => IsCurrentTosAgreed && IsCurrentPrivacyAgreed;
        public bool IsInstallAcknowledged =>
            MyOnboardState.IsWelcomed &&
            IsInstalledVersionAcknowledged;

        public string ManifestVersion {
            get {
                string version = "unknown";
                var manifestJsonElement = webExtensionsApi.Runtime.GetManifest();
                if (manifestJsonElement.TryGetProperty("version", out JsonElement versionElement) && versionElement.ValueKind == JsonValueKind.String) {
                    version = versionElement.ToString();
                }
                return version;
            }
        }

        public bool IsInstalledVersionAcknowledged =>
            MyOnboardState.InstallVersionAcknowledged == ManifestVersion;

        /// <summary>
        /// Waits for AppCache to satisfy all provided assertion functions.
        /// Polls at regular intervals until all assertions return true or timeout occurs.
        /// Used to prevent race conditions where navigation or logic happens before AppCache storage observers fire.
        /// </summary>
        /// <param name="assertions">List of boolean functions to evaluate. All must return true for success.</param>
        /// <param name="maxWaitMs">Maximum time to wait in milliseconds. Default: 5000ms (5 seconds).</param>
        /// <param name="pollIntervalMs">Polling interval in milliseconds. Default: 50ms.</param>
        /// <returns>True if all assertions passed within timeout, false otherwise.</returns>
        // TODO P2: adjust default timeouts as needed based on real-world performance
        public async Task<bool> WaitForAppCache(List<Func<bool>> assertions, int maxWaitMs = 5000, int pollIntervalMs = 500) {
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
            keriaConnectConfigsObserver?.Dispose();
            keriaConnectionInfoObserver?.Dispose();
            pendingBwAppRequestsObserver?.Dispose();
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
                // Legacy observer for single KeriaConnectConfig - kept for transition period
                // MyKeriaConnectConfig is now a computed property from KeriaConnectConfigs
                keriaConnectConfigObserver = new StorageObserver<KeriaConnectConfig>(
                    storageService,
                    StorageArea.Local,
                    onNext: (value) => {
                        // Note: MyKeriaConnectConfig is now computed from KeriaConnectConfigs
                        // This observer is kept for backward compatibility during transition
                        _logger.LogInformation("AppCache observed legacy KeriaConnectConfig change");
                        Changed?.Invoke();
                    },
                    onError: ex => _logger.LogError(ex, "Error observing legacy Keria connect config storage"),
                    null,
                    _logger
                );
                keriaConnectConfigsObserver = new StorageObserver<KeriaConnectConfigs>(
                    storageService,
                    StorageArea.Local,
                    onNext: (value) => {
                        MyKeriaConnectConfigs = value;
                        _logger.LogInformation("AppCache updated MyKeriaConnectConfigs: count={Count}", value.Configs.Count);
                        Changed?.Invoke();
                    },
                    onError: ex => _logger.LogError(ex, "Error observing KeriaConnectConfigs storage"),
                    null,
                    _logger
                );
                keriaConnectionInfoObserver = new StorageObserver<KeriaConnectionInfo>(
                    storageService,
                    StorageArea.Session,
                    onNext: (value) => {
                        MyKeriaConnectionInfo = value;
                        _logger.LogInformation("AppCache updated MyKeriaConnectionInfo");
                        // Validate that SelectedPrefix (from config) is among the fetched identifiers
                        ValidateSelectedPrefixAmongIdentifiers();
                        Changed?.Invoke();
                    },
                    onError: ex => _logger.LogError(ex, "Error observing Keria connection info storage"),
                    null,
                    _logger
                );
                pendingBwAppRequestsObserver = new StorageObserver<PendingBwAppRequests>(
                    storageService,
                    StorageArea.Session,
                    onNext: (value) => {
                        MyPendingBwAppRequests = value;
                        _logger.LogInformation("AppCache updated MyPendingBwAppRequests: count={Count}", value.Count);
                        Changed?.Invoke();
                    },
                    onError: ex => _logger.LogError(ex, "Error observing pending BW→App requests storage"),
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
            await WaitForBwReadyOrThrowAsync();

            // Fetch essential records
            // These should "must" exist after BackgroundWorker.InitializeStorageDefaultsAsync() runs

            // 1. Preferences
            var prefsResult = await storageService.GetItem<Preferences>();
            if (prefsResult.IsSuccess && prefsResult.Value is not null) {
                MyPreferences = prefsResult.Value;
                _logger.LogDebug("AppCache: Initial fetch - Preferences loaded (IsStored={IsStored})", prefsResult.Value.IsStored);
            }
            else {
                // leaving default (without IsStored = true)
                _logger.LogWarning("AppCache: Initial fetch - Preferences not found or failed, using default");
            }

            // 2. OnboardState
            var onboardResult = await storageService.GetItem<OnboardState>();
            if (onboardResult.IsSuccess && onboardResult.Value is not null) {
                MyOnboardState = onboardResult.Value;
                _logger.LogDebug("AppCache: Initial fetch - OnboardState loaded (IsStored={IsStored}, IsWelcomed={IsWelcomed})",
                    onboardResult.Value.IsStored, onboardResult.Value.IsWelcomed);
            }
            else {
                // leaving default (without IsStored = true)
                _logger.LogWarning("AppCache: Initial fetch - OnboardState not found or failed, using default");
            }

            // 3. KeriaConnectConfigs (may not exist on first run - created by ConfigurePage)
            var configsResult = await storageService.GetItem<KeriaConnectConfigs>();
            if (configsResult.IsSuccess && configsResult.Value is not null) {
                MyKeriaConnectConfigs = configsResult.Value;
                _logger.LogDebug("AppCache: Initial fetch - KeriaConnectConfigs loaded (count={Count})",
                    configsResult.Value.Configs.Count);
            }
            else {
                // leaving default (empty configs)
                _logger.LogDebug("AppCache: Initial fetch - KeriaConnectConfigs not found (expected on first run)");
            }

            /*
            // 3b. Legacy KeriaConnectConfig (for backward compatibility during transition)
            var legacyConfigResult = await storageService.GetItem<KeriaConnectConfig>();
            if (legacyConfigResult.IsSuccess && legacyConfigResult.Value is not null) {
                _logger.LogDebug("AppCache: Initial fetch - Legacy KeriaConnectConfig found (AdminUrl={AdminUrl})",
                    legacyConfigResult.Value.AdminUrl ?? "(null)");
                // Note: MyKeriaConnectConfig is now computed from KeriaConnectConfigs
            }
            else {
                _logger.LogDebug("AppCache: Initial fetch - Legacy KeriaConnectConfig not found");
            }
            */

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

            // 6. PendingBwAppRequests (pending requests from BackgroundWorker)
            var pendingRequestsResult = await storageService.GetItem<PendingBwAppRequests>(StorageArea.Session);
            if (pendingRequestsResult.IsSuccess && pendingRequestsResult.Value is not null) {
                MyPendingBwAppRequests = pendingRequestsResult.Value;
                _logger.LogDebug("AppCache: Initial fetch - PendingBwAppRequests loaded (count={Count})",
                    pendingRequestsResult.Value.Count);
            }
            else {
                _logger.LogDebug("AppCache: Initial fetch - PendingBwAppRequests not found (none pending)");
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
            await WaitForBwReadyOrThrowAsync();
            await Initialize();
        }

        /// <summary>
        /// Wait for BackgroundWorker to complete its initialization first
        /// This ensures storage defaults exist and expired sessions are cleared
        /// </summary>
        /// <returns></returns>
        /// <exception cref="TimeoutException"></exception>
        private async Task WaitForBwReadyOrThrowAsync() {
            IsBwReady = await WaitForBwReadyAsync();
            if (!IsBwReady) {
                _logger.LogError("AppCache: BackgroundWorker did not become ready within timeout - proceeding anyway");
                throw new TimeoutException("BackgroundWorker did not become ready within timeout");
            }
            return;
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
