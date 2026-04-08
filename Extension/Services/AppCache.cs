namespace Extension.Services {
    using System.Text.Json;
    using Extension.Helper;
    using Extension.Models;
    using Extension.Models.Storage;
    using Extension.Services.SignifyService.Models;
    using Extension.Services.Storage;
    using WebExtensions.Net;



    /// <summary>
    /// AppCache provides reactive access to application state stored in browser storage, including some derived properties.
    /// Note: this is not a full state management solution, but a lightweight reactive cache over storage.
    /// Note: remember to call Initialize() after construction to start observing storage changes.
    /// Note: The Change event is raised on any relevant storage change, so this will cause re-renders in dependent components, perhaps more than needed.
    /// The rendering triggering can be optimized by making the comparisons more granular if/as needed or adding more kinds of change events.
    ///
    /// ## Subscription Architecture
    ///
    /// LAYOUTS subscribe to AppCache in BaseLayout.razor:
    ///   - BaseLayout subscribes and handles app-wide concerns (auth state, session timeout, pending requests)
    ///   - MainLayout and DialogLayout inherit from BaseLayout (subscription inherited)
    ///   - Layout's StateHasChanged cascades to child components but may not re-render pages
    ///
    /// PAGES may also subscribe to ensure they receive StateHasChanged directly:
    ///   - In Blazor, layout StateHasChanged doesn't always re-render child pages
    ///   - Pages that display AppCache data should subscribe for reliable updates
    ///
    /// ## Usage on Razor Pages
    ///
    /// For REACTIVE pages (auto-update on storage changes):
    ///   @inject AppCache appCache
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
    ///   @inject AppCache appCache  // Non-reactive unless SubscribeToAppCache() is added
    ///
    /// ## IMPORTANT: Avoiding Redundant StateHasChanged
    ///
    /// SubscribeToAppCache ALREADY calls StateHasChanged automatically before invoking the callback.
    /// DO NOT add StateHasChanged in your callback - it's redundant and wastes render cycles:
    ///
    ///   // WRONG - redundant StateHasChanged:
    ///   await this.SubscribeToAppCache(appCache, async () => await InvokeAsync(StateHasChanged));
    ///
    ///   // CORRECT - simple subscription (StateHasChanged called automatically):
    ///   await this.SubscribeToAppCache(appCache);
    ///
    ///   // CORRECT - callback with meaningful work (StateHasChanged already called):
    ///   await this.SubscribeToAppCache(appCache, async () => {
    ///       RefreshData();  // Do actual work, not just StateHasChanged
    ///   });
    ///
    /// Similarly, derived layouts (MainLayout, DialogLayout) should NOT call StateHasChanged
    /// after base.HandleAppCacheChanged() since the base already calls it.
    ///
    /// See TestPage for how the IsFoo... reactive properties relate.
    /// See AppCacheComponentExtensions.cs for the SubscribeToAppCache extension method.
    ///
    /// </summary>
    /// <param name="storageGateway"></param>
    /// <param name="logger"></param>
    /// <param name="webExtensionsApi"></param>
    public class AppCache(IStorageGateway storageGateway, ILogger<AppCache> logger, IWebExtensionsApi webExtensionsApi) : IDisposable {
        private readonly IStorageGateway storageGateway = storageGateway;
        private readonly ILogger<AppCache> _logger = logger;
        internal ILogger Logger => _logger;
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
        /// Last session storage sequence number processed by BatchObserver.
        /// BW increments this on every session write; WaitForStorageSync polls it.
        /// Access via Interlocked for thread safety (volatile not available for long on 32-bit).
        /// </summary>
        private long _lastProcessedSeq;


        // Batch observer subscriptions — single BatchObserver instance registered for both areas.
        private IDisposable? _localBatchSubscription;
        private IDisposable? _sessionBatchSubscription;

        // Base properties with default values
        public Preferences MyPreferences { get; private set; } = AppConfig.DefaultPreferences;
        public OnboardState MyOnboardState { get; private set; } = new OnboardState();
        public SessionStateModel MySessionState { get; private set; } = new SessionStateModel() {
            SessionExpirationUtc = DateTime.MinValue // intentionally expired until set
        };
        public static KeriaConnectConfig DefaultKeriaConnectConfig => new KeriaConnectConfig();

        /// <summary>
        /// Pending migration notice (schema version mismatches during upgrade).
        /// Non-null when one or more prior records were discarded due to version changes.
        /// Cleared after user acknowledges via AcknowledgeMigrationNoticeAsync.
        /// </summary>
        public MigrationNotice? MyMigrationNotice { get; private set; }

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
            KeriaConnectionDigest = ""
        };

        public CachedIdentifiers MyCachedIdentifiers { get; private set; } = new CachedIdentifiers {
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

        public List<Connection> MyConnections => MyKeriaConnectConfig.Connections;

        public CachedNotifications MyNotifications { get; private set; } = new CachedNotifications();

        /// <summary>
        /// Eagerly deserialized credentials from session storage.
        /// Populated on initial fetch and updated on every CachedCredentials batch change.
        /// Pages can read this directly instead of re-deserializing from raw storage.
        /// </summary>
        public IReadOnlyList<RecursiveDictionary> MyCachedCredentials { get; private set; } = [];

        public PollingState MyPollingState { get; private set; } = new PollingState();

        public NetworkState MyNetworkState { get; private set; } = new NetworkState();

        /// <summary>
        /// Whether the browser reports network connectivity (from BackgroundWorker via session storage).
        /// </summary>
        public bool IsNetworkOnline => MyNetworkState.IsOnline;

        /// <summary>
        /// Whether the KERIA agent endpoint is reachable (from BackgroundWorker via session storage).
        /// </summary>
        public bool IsKeriaReachable => MyNetworkState.IsKeriaReachable;

        public Dictionary<string, string> MyCachedExns { get; private set; } = [];

        public List<WebsiteConfig> MyWebsiteConfigs => MyKeriaConnectConfig.WebsiteConfigs;

        /// <summary>
        /// In-memory menu open/collapse state, not persisted to storage.
        /// Initialized from IsMenuOpenInTabOnStartup / IsMenuOpenInSidePanelOnStartup preference.
        /// </summary>
        public bool IsMenuOpen { get; set; }

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
            return GetConfigByDigest(MyPreferences.SelectedKeriaConnectionDigest);
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
        /// <summary>
        /// Developer/test helper: injects a fake MigrationNotice into in-memory state so the
        /// banner component renders. Does NOT touch storage — dismissal via the banner will
        /// attempt to send the acknowledge event to BW (harmless if BW has no notice to clear).
        /// Not for production use.
        /// </summary>
        public void SetMigrationNoticeForTesting(List<string> discardedTypeNames) {
            MyMigrationNotice = new MigrationNotice { DiscardedTypeNames = discardedTypeNames };
            _logger.LogInformation(nameof(AppCache) + ": Test MigrationNotice injected with {Count} types", discardedTypeNames.Count);
            Changed?.Invoke();
        }

        /// <summary>
        /// Clears the MigrationNotice from in-memory state immediately, so the banner hides.
        /// The actual storage removal is performed by the BackgroundWorker when it receives
        /// the RequestAcknowledgeMigrationNotice event from the caller. AppCache does not
        /// write to Local storage — BW is authoritative for migration state.
        /// </summary>
        public void ClearMigrationNoticeLocal() {
            MyMigrationNotice = null;
            Changed?.Invoke();
        }

        /// <summary>
        /// Synchronously clears session-related in-memory cached properties so
        /// IsAuthenticated becomes false immediately, rather than waiting for async
        /// storage.onChanged. This is a local-only cache clear — it does not write to
        /// storage. The authoritative session clear is in SessionManager.
        ///
        /// NOTE: This resets the same session properties that BatchObserver populates
        /// from Session storage. It intentionally does NOT clear Local-storage properties
        /// (MyPreferences, MyOnboardState, MyKeriaConnectConfigs, etc.) or BwReadyState.
        /// </summary>
        [Obsolete("Prefer WaitForAppCache after BW-mediated session clear. This local-only cache clear can race with storage.onChanged re-hydration.")]
        public void ClearSessionState() {
            MySessionState = new SessionStateModel { SessionExpirationUtc = DateTime.MinValue };
            MyKeriaConnectionInfo = new KeriaConnectionInfo { KeriaConnectionDigest = "" };
            MyCachedIdentifiers = new CachedIdentifiers { IdentifiersList = [] };
            MyPendingBwAppRequests = PendingBwAppRequests.Empty;
            MyNotifications = new CachedNotifications();
            MyCachedCredentials = [];
            MyPollingState = new PollingState();
            MyCachedExns = [];
            _logger.LogDebug(nameof(AppCache) + ": Cleared session state synchronously");
            Changed?.Invoke();
        }

        /// <summary>
        /// Validates that the session's KeriaConnectionDigest matches the selected preference.
        /// Throws InvalidOperationException if there's a mismatch (indicates session/preference desync).
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when session digest doesn't match preference.</exception>
        public void ValidateSessionDigestMatchesPreference() {
            var sessionDigest = MyKeriaConnectionInfo.KeriaConnectionDigest;
            var preferenceDigest = MyPreferences.SelectedKeriaConnectionDigest;

            // Empty session digest is valid (no active session)
            if (string.IsNullOrEmpty(sessionDigest)) {
                return;
            }

            // If there's an active session, its digest must match the preference
            if (sessionDigest != preferenceDigest) {
                _logger.LogError(nameof(ValidateSessionDigestMatchesPreference) + ": Session KeriaConnectionDigest '{SessionDigest}' does not match preference '{PreferenceDigest}'",
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
            var preferenceDigest = MyPreferences.SelectedKeriaConnectionDigest;

            if (string.IsNullOrEmpty(connectionDigest)) {
                // No connection info yet, skip validation
                return true;
            }

            if (connectionDigest != preferenceDigest) {
                // Connection info is from a different config than the selected one
                // This is expected during config switches - skip validation
                _logger.LogDebug(
                    nameof(ValidateSelectedPrefixAmongIdentifiers) + ": Skipping SelectedPrefix validation: connection digest '{ConnectionDigest}' != preference digest '{PreferenceDigest}'",
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
            var identifiersList = MyCachedIdentifiers?.IdentifiersList;
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
                    nameof(ValidateSelectedPrefixAmongIdentifiers) + ": SelectedPrefix '{SelectedPrefix}' is not among the fetched identifiers for config '{Digest}'. " +
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
                var identifiersList = MyCachedIdentifiers?.IdentifiersList;
                if (identifiersList is null || identifiersList.Count == 0) {
                    return EmptyAidsList;
                }
                return identifiersList.First().Aids ?? EmptyAidsList;
            }
        }

        public bool IsReadyToTransact => IsNotWaiting && IsConnectedToKeria;

        public bool IsConnectedToKeria => IsIdentifierFetched && IsAuthenticated;

        public bool IsIdentifierFetched =>
            MyKeriaConnectConfig.AgentAidPrefix is not null &&
            MyCachedIdentifiers?.IdentifiersList?.Count > 0;
        public bool IsAuthenticated => IsSessionUnlocked && IsInitialized;

        public bool ShowedGettingStarted => MyOnboardState.ShowedGettingStarted;

        public bool IsNotWaiting =>
            IsNotWaitingOnKeria &&
            IsNotWaitingOnUser &&
            IsNotWaitingOnPendingRequests &&
            IsBwNotWaitingOnApp &&
            IsAppNotWaitingOnBw;
        // TODO P3: implement real user NotWaiting... tracking. Example: outstanding Request from CS.  Dialog open. OOBI in progress, etc.

        public bool IsNotWaitingOnKeria { get; private set; }
        public bool IsNotWaitingOnUser { get; private set; }
        public bool IsBwNotWaitingOnApp { get; private set; }
        public bool IsAppNotWaitingOnBw { get; private set; }
        public bool IsNotWaitingOnPendingRequests { get; private set; }
        public bool IsSessionUnlocked =>
            IsPasscodeHashSet &&
            IsSessionExpirationSet &&
            IsSessionNotExpired;
        public bool IsPasscodeHashSet => MyKeriaConnectConfig.PasscodeHash != 0;
        public bool IsSessionExpirationSet => MySessionState.SessionExpirationUtc != DateTime.MinValue;
        public bool IsSessionNotExpired => MySessionState.SessionExpirationUtc > DateTime.UtcNow;
        public bool IsInitialized =>
            IsConfigured;
        public bool IsConfigured =>
            IsKeriaConfigValidated &&
            IsProductOnboarded &&
            MyPreferences.IsStored;
        // TODO P3 add other aspects of KeriaConfig validation as needed.  See also ValidateConfiguration() in KeriaConnectConfig.cs
        public bool IsSelectedKeriaConnectionDigestStored =>
            !string.IsNullOrEmpty(MyPreferences.SelectedKeriaConnectionDigest) &&
            MyKeriaConnectConfigs.Configs.ContainsKey(MyPreferences.SelectedKeriaConnectionDigest);

        // TODO P3 add other aspects of KeriaConfig validation as needed.  See also ValidateConfiguration() in KeriaConnectConfig.cs
        public bool IsKeriaConfigValidated =>
            IsSelectedKeriaConnectionDigestStored &&
            !string.IsNullOrEmpty(MyKeriaConnectConfig.AdminUrl) &&
            !string.IsNullOrWhiteSpace(MyKeriaConnectConfig.ClientAidPrefix) &&
            !string.IsNullOrWhiteSpace(MyKeriaConnectConfig.AgentAidPrefix) &&
            !(MyKeriaConnectConfig.PasscodeHash == 0);

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

        /// <summary>
        /// Logs every component of the IsAuthenticated chain for diagnosing unexpected session locks.
        /// Call when IsAuthenticated transitions to false unexpectedly.
        /// </summary>
        public void LogAuthDiagnostic(ILogger logger) {
            var selectedDigest = MyPreferences.SelectedKeriaConnectionDigest;
            var configFound = !string.IsNullOrEmpty(selectedDigest) && MyKeriaConnectConfigs.Configs.ContainsKey(selectedDigest);
            logger.LogWarning(
                "AUTH DIAGNOSTIC: IsAuthenticated={IsAuth}, IsSessionUnlocked={IsUnlocked}, IsInitialized={IsInit}" +
                " | SessionUnlocked components: IsPasscodeHashSet={PHSet}(hash={PH}), IsSessionExpirationSet={SESet}, IsSessionNotExpired={SNE}(expUtc={Exp})" +
                " | Initialized components: IsKeriaConfigValidated={KCV}, IsProductOnboarded={PO}, MyPreferences.IsStored={PS}" +
                " | Config lookup: selectedDigest={Digest}, configFoundInDict={CF}, configCount={CC}" +
                " | ProductOnboarded: IsWelcomed={IW}, InstallVersionAcknowledged={IVA}, TosHash={TH}=={ETH}, PrivacyHash={PRH}=={EPH}",
                IsAuthenticated, IsSessionUnlocked, IsInitialized,
                IsPasscodeHashSet, MyKeriaConnectConfig.PasscodeHash, IsSessionExpirationSet, IsSessionNotExpired, MySessionState.SessionExpirationUtc,
                IsKeriaConfigValidated, IsProductOnboarded, MyPreferences.IsStored,
                selectedDigest ?? "(null)", configFound, MyKeriaConnectConfigs.Configs.Count,
                MyOnboardState.IsWelcomed, MyOnboardState.InstallVersionAcknowledged,
                MyOnboardState.TosAgreedHash, AppConfig.ExpectedTermsDigest,
                MyOnboardState.PrivacyAgreedHash, AppConfig.ExpectedPrivacyDigest);
        }

        public bool IsCurrentTosAgreed =>
            IsTosAgreedUtc &&
            IsTosHashExpected;
        public bool IsTosAgreedUtc => MyOnboardState.TosAgreedUtc is not null;
        public bool IsTosHashExpected => MyOnboardState.TosAgreedHash == AppConfig.ExpectedTermsDigest;
        public bool IsCurrentPrivacyAgreed =>
            IsPrivacyAgreedUtc &&
            IsPrivacyHashExpected;
        public bool IsPrivacyAgreedUtc => MyOnboardState.PrivacyAgreedUtc is not null;
        public bool IsPrivacyHashExpected => MyOnboardState.PrivacyAgreedHash == AppConfig.ExpectedPrivacyDigest;
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
        /// <param name="pollIntervalMs">Polling interval in milliseconds. Default: 200ms.</param>
        /// <returns>True if all assertions passed within timeout, false otherwise.</returns>
        public async Task<bool> WaitForAppCache(List<Func<bool>> assertions, int maxWaitMs = AppConfig.WaitForAppCacheTimeoutMs, int pollIntervalMs = AppConfig.WaitForAppCachePollIntervalMs) {
            if (assertions is null || assertions.Count == 0) {
                _logger.LogWarning(nameof(WaitForAppCache) + ": called with no assertions");
                return true; // No assertions means nothing to wait for
            }

            var elapsedMs = 0;

            while (elapsedMs < maxWaitMs) {
                // Check if all assertions pass
                if (assertions.All(assertion => assertion())) {
                    _logger.LogDebug(nameof(WaitForAppCache) + ": assertions all passed after {ElapsedMs}ms", elapsedMs);
                    return true;
                }

                await Task.Delay(pollIntervalMs);
                elapsedMs += pollIntervalMs;
            }

            _logger.LogWarning(nameof(WaitForAppCache) + ": assertions did not all pass after {ElapsedMs}ms timeout", elapsedMs);
            return false;
        }

        /// <summary>
        /// Waits until AppCache has processed a NEW session storage write from BW.
        /// Snapshots the current _lastProcessedSeq, then polls storage until a higher
        /// sequence appears AND AppCache's BatchObserver has processed it.
        /// If a condition is provided, keeps waiting (advancing the baseline) until the
        /// condition is also satisfied — this ensures the specific BW operation (not just
        /// any write) has propagated.
        /// Returns true if synced within timeout, false on timeout.
        /// </summary>
        // TODO P2: Consider extending sequence sync to Local storage area.
        public async Task<bool> WaitForStorageSync(Func<bool>? condition = null, int maxWaitMs = AppConfig.WaitForAppCacheTimeoutMs) {
            var baselineSeq = Interlocked.Read(ref _lastProcessedSeq);

            var elapsed = 0;
            while (elapsed < maxWaitMs) {
                await Task.Delay(AppConfig.WaitForAppCachePollIntervalMs);
                elapsed += AppConfig.WaitForAppCachePollIntervalMs;

                // Read current sequence from storage (bypassing cache)
                var result = await storageGateway.GetItem<SessionSequence>(StorageArea.Session);
                var storedSeq = result.IsSuccess ? result.Value?.Seq ?? 0 : 0;

                // Synced when: storage has a newer sequence than our baseline
                // AND AppCache has processed at least up to that sequence
                if (storedSeq > baselineSeq && Interlocked.Read(ref _lastProcessedSeq) >= storedSeq) {
                    if (condition is null || condition()) {
                        _logger.LogDebug(nameof(WaitForStorageSync) + ": synced to seq {Stored} (baseline was {Baseline}) after {ElapsedMs}ms", storedSeq, baselineSeq, elapsed);
                        return true;
                    }
                    // A new seq arrived but condition not met — advance baseline and keep waiting
                    // for the next BW write (e.g., waiting for Lock clear, not just UserActivity extend)
                    baselineSeq = storedSeq;
                }
            }

            _logger.LogWarning(nameof(WaitForStorageSync) + ": timed out (baseline={Baseline}, lastProcessed={Current}, maxWait={MaxWaitMs}ms)", baselineSeq, Interlocked.Read(ref _lastProcessedSeq), maxWaitMs);
            return false;
        }

        /// <summary>
        /// Batch observer for all storage records. A single instance is registered for
        /// both Local and Session areas. Fires Changed exactly once per Chrome onChanged batch.
        /// </summary>
        private sealed class BatchObserver(AppCache cache) : IStorageBatchObserver {
            public void OnBatch(StorageArea batchArea, StorageChangeBatch batch) {
                bool dirty = false;

                if (batchArea == StorageArea.Local) {
                    if (batch.Contains<Preferences>()) {
                        cache.MyPreferences = batch.GetNew<Preferences>() ?? AppConfig.DefaultPreferences;
                        cache._logger.LogDebug(nameof(AppCache) + ": updated MyPreferences");
                        dirty = true;
                    }
                    if (batch.Contains<OnboardState>()) {
                        cache.MyOnboardState = batch.GetNew<OnboardState>() ?? new OnboardState();
                        cache._logger.LogDebug(nameof(AppCache) + ": updated MyOnboardState");
                        dirty = true;
                    }
                    if (batch.Contains<KeriaConnectConfig>()) {
                        // Legacy single-config record — MyKeriaConnectConfig is computed from KeriaConnectConfigs.
                        // Still mark dirty so pages re-render if a writer updates the legacy record.
                        cache._logger.LogDebug(nameof(AppCache) + ": observed legacy KeriaConnectConfig change");
                        dirty = true;
                    }
                    if (batch.Contains<KeriaConnectConfigs>()) {
                        cache.MyKeriaConnectConfigs = batch.GetNew<KeriaConnectConfigs>() ?? new KeriaConnectConfigs();
                        cache._logger.LogDebug(nameof(AppCache) + ": updated MyKeriaConnectConfigs (count={Count})", cache.MyKeriaConnectConfigs.Configs.Count);
                        dirty = true;
                    }
                    if (batch.Contains<MigrationNotice>()) {
                        cache.MyMigrationNotice = batch.GetNew<MigrationNotice>();
                        cache._logger.LogDebug(nameof(AppCache) + ": updated MyMigrationNotice");
                        dirty = true;
                    }
                }
                else if (batchArea == StorageArea.Session) {
                    if (batch.Contains<SessionStateModel>()) {
                        var value = batch.GetNew<SessionStateModel>() ?? new SessionStateModel { SessionExpirationUtc = DateTime.MinValue };
                        cache.MySessionState = value;
                        cache._logger.LogDebug(nameof(AppCache) + ": updated MySessionState: SessionExpirationUtc={Expiration}", value.SessionExpirationUtc);
                        dirty = true;
                    }
                    if (batch.Contains<KeriaConnectionInfo>()) {
                        cache.MyKeriaConnectionInfo = batch.GetNew<KeriaConnectionInfo>() ?? new KeriaConnectionInfo { KeriaConnectionDigest = "" };
                        cache._logger.LogDebug(nameof(AppCache) + ": updated MyKeriaConnectionInfo");
                        dirty = true;
                    }
                    if (batch.Contains<CachedIdentifiers>()) {
                        cache.MyCachedIdentifiers = batch.GetNew<CachedIdentifiers>() ?? new CachedIdentifiers { IdentifiersList = [] };
                        cache._logger.LogDebug(nameof(AppCache) + ": updated MyCachedIdentifiers (count={Count})", cache.MyCachedIdentifiers.IdentifiersList.Count);
                        cache.ValidateSelectedPrefixAmongIdentifiers();
                        dirty = true;
                    }
                    if (batch.Contains<PendingBwAppRequests>()) {
                        cache.MyPendingBwAppRequests = batch.GetNew<PendingBwAppRequests>() ?? PendingBwAppRequests.Empty;
                        cache._logger.LogDebug(nameof(AppCache) + ": updated MyPendingBwAppRequests (count={Count})", cache.MyPendingBwAppRequests.Count);
                        dirty = true;
                    }
                    if (batch.Contains<CachedNotifications>()) {
                        cache.MyNotifications = batch.GetNew<CachedNotifications>() ?? new CachedNotifications();
                        cache._logger.LogDebug(nameof(AppCache) + ": updated MyNotifications (count={Count})", cache.MyNotifications.Items.Count);
                        dirty = true;
                    }
                    if (batch.Contains<CachedCredentials>()) {
                        cache.UpdateCachedCredentialsFromBatch(batch);
                        cache._logger.LogDebug(nameof(AppCache) + ": updated MyCachedCredentials (count={Count})", cache.MyCachedCredentials.Count);
                        dirty = true;
                    }
                    if (batch.Contains<PollingState>()) {
                        cache.MyPollingState = batch.GetNew<PollingState>() ?? new PollingState();
                        cache._logger.LogDebug(nameof(AppCache) + ": updated MyPollingState");
                        dirty = true;
                    }
                    if (batch.Contains<CachedExns>()) {
                        var raw = batch.GetNew<CachedExns>();
                        cache.MyCachedExns = raw?.Exchanges ?? [];
                        cache._logger.LogDebug(nameof(AppCache) + ": updated MyCachedExns (count={Count})", cache.MyCachedExns.Count);
                        dirty = true;
                    }
                    if (batch.Contains<NetworkState>()) {
                        cache.MyNetworkState = batch.GetNew<NetworkState>() ?? new NetworkState();
                        cache._logger.LogDebug(nameof(AppCache) + ": updated MyNetworkState: IsOnline={IsOnline}, IsKeriaReachable={IsKeriaReachable}", cache.MyNetworkState.IsOnline, cache.MyNetworkState.IsKeriaReachable);
                        dirty = true;
                    }
                    if (batch.Contains<SessionSequence>()) {
                        var seq = batch.GetNew<SessionSequence>();
                        if (seq is not null) Interlocked.Exchange(ref cache._lastProcessedSeq, seq.Seq);
                        // No dirty flag — sequence marker is infrastructure, not UI state
                    }
                }

                if (dirty) cache.Changed?.Invoke();
            }
        }

        private void UpdateCachedCredentialsFromBatch(StorageChangeBatch batch) {
            var raw = batch.GetNew<CachedCredentials>();
            MyCachedCredentials = raw?.Credentials is { Count: > 0 } dict
                ? CredentialHelper.DeserializeCredentialsDict(dict)
                : [];
        }

        public void Dispose() {
            _localBatchSubscription?.Dispose();
            _sessionBatchSubscription?.Dispose();
            _initLock?.Dispose();
            GC.SuppressFinalize(this);
        }

        public event Action? Changed;

        public async Task Initialize() {
            // Prevent multiple initializations (singleton service may be accessed from multiple components)
            if (_isInitialized) {
                _logger.LogDebug(nameof(Initialize) + ": AppCache already initialized, skipping");
                return;
            }

            await _initLock.WaitAsync();
            try {
                if (_isInitialized) {
                    return;
                }

                _logger.LogInformation(nameof(Initialize) + ": Initializing AppCache");

                _isInitialized = true;

                // Perform initial fetch of essential storage records
                // This ensures My* properties have current values before IsReady is set
                _logger.LogInformation(nameof(AppCache) + ": Fetching initial storage values");
                await FetchInitialStorageValuesAsync();

                // Register batch observer for all storage records.
                // Single observer instance handles both areas via the batchArea parameter.
                var batchObserver = new BatchObserver(this);
                _localBatchSubscription = storageGateway.SubscribeBatch(batchObserver, StorageArea.Local);
                _sessionBatchSubscription = storageGateway.SubscribeBatch(batchObserver, StorageArea.Session);

                IsReady = true;
                _logger.LogInformation(nameof(AppCache) + ": initialization complete, IsReady=true");
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
            // BW readiness already confirmed by EnsureInitializedAsync() before Initialize() is called.
            // No need to wait again here.

            // Phase 6 of the StorageGateway migration: replace the previous 9 parallel per-type
            // GetItem<T> calls with two bulk GetItems calls (one per area). Each call makes a
            // single chrome.storage.<area>.get(keys[]) round-trip instead of 4–5 parallel ones.
            // Version-mismatch handling is preserved — StorageGateway.GetItems reports mismatches
            // in StorageReadResult.VersionMismatchKeys, and Get<T>() returns null for them.
            var localTask = storageGateway.GetItems(
                StorageArea.Local,
                typeof(Preferences),
                typeof(OnboardState),
                typeof(KeriaConnectConfigs),
                typeof(MigrationNotice));
            var sessionTask = storageGateway.GetItems(
                StorageArea.Session,
                typeof(SessionStateModel),
                typeof(KeriaConnectionInfo),
                typeof(CachedIdentifiers),
                typeof(PendingBwAppRequests),
                typeof(CachedNotifications),
                typeof(CachedCredentials),
                typeof(PollingState),
                typeof(CachedExns),
                typeof(NetworkState),
                typeof(SessionSequence));
            await Task.WhenAll(localTask, sessionTask);

            var localRes = localTask.Result;
            var sessionRes = sessionTask.Result;

            if (localRes.IsFailed) {
                _logger.LogError(nameof(AppCache) + ": Initial bulk Local read failed: {Errors}",
                    string.Join("; ", localRes.Errors.Select(e => e.Message)));
            }
            if (sessionRes.IsFailed) {
                _logger.LogError(nameof(AppCache) + ": Initial bulk Session read failed: {Errors}",
                    string.Join("; ", sessionRes.Errors.Select(e => e.Message)));
            }

            var local = localRes.IsSuccess ? localRes.Value : null;
            var session = sessionRes.IsSuccess ? sessionRes.Value : null;

            // Apply results — Local storage records
            var prefs = local?.Get<Preferences>();
            if (prefs is not null) {
                MyPreferences = prefs;
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - Preferences loaded (IsStored={IsStored})", prefs.IsStored);
            }
            else {
                _logger.LogWarning(nameof(AppCache) + ": Initial fetch - Preferences not found or failed, using default");
            }

            var onboard = local?.Get<OnboardState>();
            if (onboard is not null) {
                MyOnboardState = onboard;
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - OnboardState loaded (IsStored={IsStored}, IsWelcomed={IsWelcomed})",
                    onboard.IsStored, onboard.IsWelcomed);
            }
            else {
                _logger.LogWarning(nameof(AppCache) + ": Initial fetch - OnboardState not found or failed, using default");
            }

            var configs = local?.Get<KeriaConnectConfigs>();
            if (configs is not null) {
                MyKeriaConnectConfigs = configs;
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - KeriaConnectConfigs loaded (count={Count})",
                    configs.Configs.Count);
            }
            else {
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - KeriaConnectConfigs not found (expected on first run)");
            }

            var migrationNotice = local?.Get<MigrationNotice>();
            if (migrationNotice is not null && migrationNotice.DiscardedTypeNames.Count > 0) {
                MyMigrationNotice = migrationNotice;
                _logger.LogWarning(nameof(AppCache) + ": MigrationNotice present - prior data was discarded: {Types}",
                    string.Join(", ", migrationNotice.DiscardedTypeNames));
            }

            // Apply results — Session storage records (may not exist if session is locked or browser was restarted)
            var sessionState = session?.Get<SessionStateModel>();
            if (sessionState is not null) {
                MySessionState = sessionState;
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - SessionStateModel loaded (SessionExpirationUtc={Expiration})",
                    sessionState.SessionExpirationUtc);
            }
            else {
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - SessionStateModel not found (session locked or new)");
            }

            var connectionInfo = session?.Get<KeriaConnectionInfo>();
            if (connectionInfo is not null) {
                MyKeriaConnectionInfo = connectionInfo;
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - KeriaConnectionInfo loaded");
            }
            else {
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - KeriaConnectionInfo not found (not connected)");
            }

            var cachedIdentifiers = session?.Get<CachedIdentifiers>();
            if (cachedIdentifiers is not null) {
                MyCachedIdentifiers = cachedIdentifiers;
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - CachedIdentifiers loaded (count={Count})",
                    cachedIdentifiers.IdentifiersList.Count);
            }
            else {
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - CachedIdentifiers not found (not connected)");
            }

            var pendingRequests = session?.Get<PendingBwAppRequests>();
            if (pendingRequests is not null) {
                MyPendingBwAppRequests = pendingRequests;
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - PendingBwAppRequests loaded (count={Count})",
                    pendingRequests.Count);
            }
            else {
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - PendingBwAppRequests not found (none pending)");
            }

            var notifications = session?.Get<CachedNotifications>();
            if (notifications is not null) {
                MyNotifications = notifications;
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - Notifications loaded (count={Count})",
                    notifications.Items.Count);
            }
            else {
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - Notifications not found (none stored)");
            }

            // Apply results — New sub-cache records (Phase group 2)

            var cachedCreds = session?.Get<CachedCredentials>();
            if (cachedCreds?.Credentials is { Count: > 0 } credsDict) {
                MyCachedCredentials = CredentialHelper.DeserializeCredentialsDict(credsDict);
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - CachedCredentials loaded and deserialized (count={Count})",
                    MyCachedCredentials.Count);
            }
            else {
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - CachedCredentials not found or empty");
            }

            var pollingState = session?.Get<PollingState>();
            if (pollingState is not null) {
                MyPollingState = pollingState;
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - PollingState loaded");
            }
            else {
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - PollingState not found");
            }

            var cachedExns = session?.Get<CachedExns>();
            if (cachedExns?.Exchanges is { Count: > 0 } exnsDict) {
                MyCachedExns = exnsDict;
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - CachedExns loaded (count={Count})", MyCachedExns.Count);
            }
            else {
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - CachedExns not found or empty");
            }

            var networkState = session?.Get<NetworkState>();
            if (networkState is not null) {
                MyNetworkState = networkState;
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - NetworkState loaded (IsOnline={IsOnline}, IsKeriaReachable={IsKeriaReachable})", networkState.IsOnline, networkState.IsKeriaReachable);
            }
            else {
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - NetworkState not found (assuming online)");
            }

            var sessionSeq = session?.Get<SessionSequence>();
            if (sessionSeq is not null) {
                _lastProcessedSeq = sessionSeq.Seq;
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - SessionSequence loaded (Seq={Seq})", sessionSeq.Seq);
            }
            else {
                _logger.LogDebug(nameof(AppCache) + ": Initial fetch - SessionSequence not found");
            }

            _logger.LogInformation(nameof(AppCache) + ": Initial fetch complete (2 bulk reads: Local 5 keys, Session 10 keys)");
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
                _logger.LogDebug(nameof(AppCache) + ": EnsureInitializedAsync - already ready");
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
                _logger.LogError(nameof(AppCache) + ": BackgroundWorker did not become ready within timeout - proceeding anyway");
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
            _logger.LogDebug(nameof(AppCache) + ": Waiting for BackgroundWorker initialization (timeout: {TimeoutMs}ms)", AppConfig.BwReadyTimeoutMs);

            var elapsedMs = 0;

            while (elapsedMs < AppConfig.BwReadyTimeoutMs) {
                var result = await storageGateway.GetItem<BwReadyState>(StorageArea.Session);

                if (result.IsSuccess && result.Value?.IsInitialized == true) {
                    _logger.LogDebug(
                        nameof(AppCache) + ": BackgroundWorker ready after {ElapsedMs}ms (initialized at {InitializedAt})",
                        elapsedMs,
                        result.Value.InitializedAtUtc);
                    return true;
                }

                await Task.Delay(AppConfig.BwReadyPollIntervalMs);
                elapsedMs += AppConfig.BwReadyPollIntervalMs;
            }

            _logger.LogWarning(
                nameof(AppCache) + ": Timeout after {TimeoutMs}ms - BackgroundWorker did not become ready. " +
                "App will proceed but may encounter stale or missing storage data.",
                AppConfig.BwReadyTimeoutMs);
            return false;
        }
    }
}
