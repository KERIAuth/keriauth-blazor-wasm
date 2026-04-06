using Extension.Models;
using MudBlazor;
using MudBlazor.Utilities;

namespace Extension {
    public static class AppConfig {
        // --- Signify-ts / KERIA timeout hierarchy ---
        // These C# timeouts wrap JS interop calls that have their own internal timeouts.
        // The C# timeout must always be longer than the JS-side total to avoid premature cancellation.
        //
        // JS-side timeouts (in signifyClient.ts):
        //   DEFAULT_TIMEOUT_MS = 30000ms — used by waitAndDeleteOperation() and oobiResolve()
        //   waitForCredential  = 60000ms — polls KERIA credential store after ipexAdmitAndSubmit
        //
        // For simple signify calls (get, list, mark, delete):
        //   SignifyTimeoutMs (20s) > typical KERIA round-trip (~1-5s)
        //
        // For composite operations (*AndSubmit, CreateAidWithEndRole, IssueAndGetCredential, etc.):
        //   SignifyLongOperationTimeoutMs (120s) > JS worst case:
        //     waitAndDeleteOperation (30s) + waitForCredential (60s) = 90s
        //     Plus margin for event-loop contention
        //
        // Note there is also a default InactivityTimeout in service-worker.ts that should be overridden by this during app startup
        public const int SignifyTimeoutMs = 20000; // For simple get/list/mark/delete calls
        public const int SignifyLongOperationTimeoutMs = 120000; // For composite operations involving KERIA coordination
        public static readonly TimeSpan SignifyTimeout = TimeSpan.FromMilliseconds(SignifyTimeoutMs);
        public static readonly TimeSpan SignifyLongOperationTimeout = TimeSpan.FromMilliseconds(SignifyLongOperationTimeoutMs);

        // SessionManager configuration
        public const string SessionManagerAlarmName = "SessionManagerAlarm";
        public const double AlarmRescheduleThresholdSeconds = 15;
        public const double SessionKeepAliveAlarmPeriodMinutes = 0.5; // 30 seconds — Chrome 120+ minimum

        // BackgroundWorker ready-wait configuration
        public const int BwReadyTimeoutMs = 5000;
        public const int BwReadyPollIntervalMs = 200;

        // AppCache WaitForAppCache polling configuration
        // Used by ~35 call sites that poll for storage observer state to propagate.
        // Most waits resolve in <1s; timeout is a safety net.
        // Compare: BwReadyPollIntervalMs (200ms) polls for service worker readiness,
        // which involves cross-runtime messaging and is similarly fast to resolve.
        public const int WaitForAppCacheTimeoutMs = 5000;
        public const int WaitForAppCachePollIntervalMs = 200;

        // Notification polling configuration
        public const string NotificationPollAlarmName = "NotificationPollAlarm";
        public const double NotificationPollAlarmPeriodMinutes = 2.0;
        public static readonly TimeSpan NotificationPollInterval = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan NotificationBurstDuration = TimeSpan.FromSeconds(120);

        // Minimum interval between repeated fetches of the same resource.
        // Used by BackgroundWorker's refresh methods to skip redundant fetches via PollingState timestamps.
        // Values chosen to eliminate thundering-herd calls while keeping UX responsive.
        public static readonly TimeSpan IdentifiersPollSkipThreshold = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan CredentialsPollSkipThreshold = TimeSpan.FromSeconds(3);
        public static readonly TimeSpan NotificationsPollSkipThreshold = TimeSpan.FromSeconds(2);
        // Minimum interval between burst restarts — prevents thrashing when many events arrive in rapid succession.
        public static readonly TimeSpan NotificationBurstRestartCooldown = TimeSpan.FromSeconds(10);

        // Request/Response messaging configuration
        public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);

        // KERIA connection settings
        public const string LocalhostKeriaConnectAlias = "localhost";
        public const string LocalhostKeriaAdminUrl = "http://localhost:3901";
        public const string LocalhostKeriaBootUrl = "http://localhost:3903";

        public readonly struct KeriaPreset(string provider, string adminUrl, string bootUrl) {
            public string ProviderName { get; } = provider;
            public string AgentUrl { get; } = adminUrl;
            public string BootUrl { get; } = bootUrl;
        }

        // TODO P3 load presets from external source for enterprise deployment
        public static readonly List<KeriaPreset> PresetAgents = [
            new KeriaPreset("Veridian Testnet", "https://keria.veridian.dandelion.link", "https://keria-boot.veridian.dandelion.link"),
            new KeriaPreset($"{ProductName} Test Cloud (basic auth for boot)", "https://keria.cloud.dign.id", "https://keria-boot.cloud.dign.id"),
            new KeriaPreset("GLEIF Testnet", "https://keria.testnet.gleif.org:3901", "https://keria.testnet.gleif.org:3903"),
            // new KeriaPreset("Veridian Dev Testnet", "https://keria.dev.idw-sandboxes.cf-deployments.org", "https://keria-boot.dev.idw-sandboxes.cf-deployments.org"),
            new KeriaPreset("Provenant Origin Demo (basic auth for boot)", "https://origin.demo.provenant.net/v1/keria/admin", "https://origin.demo.provenant.net/v1/keria/boot"),
            new KeriaPreset("localhost", LocalhostKeriaAdminUrl, LocalhostKeriaBootUrl),
            new KeriaPreset("Custom", "", "")
        ];

        // default Preferences
        public const float DefaultInactivityTimeoutMins = 20.0f; // TODO P3 should be 10.0f;
        public const float MaxInactivityTimeoutMins = 20.0f; // TODO P3 should be 10.0f;
        // Intentionally random initial IsDarkTheme to improve discoverability
        public static bool DefaultIsDarkTheme => Random.Shared.Next(2) == 0; // false;
        public const MudBlazor.DrawerVariant DefaultDrawerVariantInTab = MudBlazor.DrawerVariant.Persistent;
        public const MudBlazor.DrawerVariant DefaultDrawerVariantInSidePanel = MudBlazor.DrawerVariant.Temporary;
        public const bool DefaultIsMenuOpenInTabOnStartup = true;
        public const bool DefaultIsMenuOpenInSidePanelOnStartup = false;
        public const bool DefaultIsPasskeyUsePreferred = true;
        public const bool DefaultBeepOnScanSuccess = true;
        public const string DefaultUserVerification = "preferred"; // "required" is most secure default
        public const string DefaultResidentKey = "preferred"; // "required" is most secure default
        public const string DefaultAuthenticatorAttachment = "undefined"; // "platform" is most secure default
        public const string DefaultAttestationConveyancePref = "direct";  // "direct" needed to get AAGUID for authenticator identification
        public static readonly List<string> DefaultAuthenticatorTransports = ["usb"]; // ["hybrid", "internal", "ble", "nfc", "usb"]; // more secure default would be ["internal", "usb"]
        public static readonly List<string> DefaultSelectedHints = []; // more secure default would be ["security-key"]
        public static readonly List<string> AvailableTransportOptions = ["usb", "nfc", "ble", "internal", "hybrid"];
        public static readonly List<string> AllHints = ["hybrid", "security-key", "client-device"];

        public static Models.Preferences DefaultPreferences => new() {
            InactivityTimeoutMinutes = DefaultInactivityTimeoutMins,
            IsDarkTheme = DefaultIsDarkTheme,
            DrawerVariantInTab = DefaultDrawerVariantInTab,
            DrawerVariantInSidePanel = DefaultDrawerVariantInSidePanel,
            IsPasskeyUsePreferred = DefaultIsPasskeyUsePreferred,
            DrawerVariantInPopup = MudBlazor.DrawerVariant.Temporary,
            IsMenuOpenInSidePanelOnStartup = DefaultIsMenuOpenInSidePanelOnStartup,
            IsMenuOpenInTabOnStartup = DefaultIsMenuOpenInTabOnStartup,
            IsSignRequestDetailShown = false,
            IsStored = false
        };

        // Other settings
        public const int DisplaySessionExpirationAtSecondsRemaining = 30;
        // Display inactivity countdown on AppBar when this many seconds are remaining
        public const int AppBarRefreshTimerDueTimeSeconds = 20;
        public static readonly TimeSpan ThrottleInactivityInterval = TimeSpan.FromSeconds(10);
        public const int ExpectedTermsDigest = -2032710982;
        public const int ExpectedPrivacyDigest = -660950952;

        // Branding
        public const string ProductName = "DIGN";
        public const string FullProductName = "DIGN Identity Wallet";
        public const string LogPrefix = "dign";
        public const string WebsiteUrl = "https://dign.id";
        public const string UninstallUrl = "https://dign.id/uninstall.html";

        public static readonly Typography Typography = new() {
            H3 = new H3Typography { FontSize = "1.35rem", LineHeight = "1.25" },
            H4 = new H4Typography { FontSize = "1.15rem", LineHeight = "1.25" },
            H5 = new H5Typography { FontSize = "1.00rem", LineHeight = "1.20" },
            H6 = new H6Typography { FontSize = "0.95rem", LineHeight = "1.20" },
            Body1 = new Body1Typography { FontSize = "0.92rem" },
            Body2 = new Body2Typography { FontSize = "0.85rem" },
        };

        // MudBlazor 9 defaults, explicit in HSLA. See https://mudblazor.com/customization/default-theme
        // Lighten/Darken variants are auto-calculated by MudBlazor — not set here.
        // ContrastText defaults to white — only override where needed.
        public static readonly MudTheme MyCustomTheme = new() {
            Typography = Typography,
            PaletteLight = new PaletteLight() {
                // Semantic colors                              // MudBlazor default hex
                Primary = new MudColor(183, 1.0, 0.30, 1.0),   // DIGN teal
                PrimaryContrastText = new MudColor(0, 0.0, 1.0, 1.0), // black — high contrast on teal
                Secondary = new MudColor(340, 0.82, 0.59, 1.0), // #EC407A
                SecondaryContrastText = new MudColor(0, 0.0, 1.0, 1.0),
                Tertiary = new MudColor(165, 0.73, 0.45, 1.0), // #1EC8A5
                TertiaryContrastText = new MudColor(0, 0.0, 1.0, 1.0),
                Info = new MudColor(207, 0.90, 0.54, 1.0),     // #2196F3
                InfoContrastText = new MudColor(0, 0.0, 1.0, 1.0),
                Success = new MudColor(145, 1.0, 0.39, 1.0),   // #00C853
                SuccessContrastText = new MudColor(0, 0.0, 1.0, 1.0),
                Warning = new MudColor(36, 1.0, 0.50, 1.0),    // #FF9800
                WarningContrastText = new MudColor(0, 0.0, 1.0, 1.0),
                Error = new MudColor(4, 0.90, 0.58, 1.0),      // #F44336
                ErrorContrastText = new MudColor(0, 0.0, 1.0, 1.0),
                Dark = new MudColor(0, 0.0, 0.26, 1.0),        // #424242
                DarkContrastText = new MudColor(0, 0.0, 1.0, 1.0),

                // Text
                TextPrimary = new MudColor(0, 0.0, 0.0, 1.0),   // black
                TextSecondary = new MudColor(0, 0.0, 0.0, 0.54),  // rgba(0,0,0,0.54)
                TextDisabled = new MudColor(0, 0.0, 0.0, 0.38),   // rgba(0,0,0,0.38)

                // Actions
                ActionDefault = new MudColor(0, 0.0, 0.0, 0.54),
                ActionDisabled = new MudColor(0, 0.0, 0.0, 0.26),
                ActionDisabledBackground = new MudColor(0, 0.0, 0.0, 0.12),

                // Surfaces                                     // Sync: #FFFFFF / #F5F5F5 in index*.html splash
                Background = new MudColor(0, 0.0, 1.0, 1.0),    // #FFFFFF
                BackgroundGray = new MudColor(0, 0.0, 0.96, 1.0), // #F5F5F5
                Surface = new MudColor(0, 0.0, 1.0, 1.0),      // #FFFFFF

                // Drawer & Appbar — matched neutral gray
                DrawerBackground = new MudColor(183, 67.0, 0.96, 1.0), // #  very light DIGN teal
                DrawerText = new MudColor(0, 0.0, 0.00, 1.0),  // black
                DrawerIcon = new MudColor(0, 0.0, 0.38, 1.0),  // #616161
                AppbarBackground = new MudColor(183, 67.0, 0.96, 1.0), // #  very light DIGN teal
                AppbarText = new MudColor(0, 0.0, 0.26, 1.0),  // #424242 — dark for contrast

                // Lines & borders
                LinesDefault = new MudColor(0, 0.0, 0.0, 0.12),
                LinesInputs = new MudColor(0, 0.0, 0.74, 1.0), // #BDBDBD
                Divider = new MudColor(0, 0.0, 0.93, 1.0),     // #EEEEEE
                DividerLight = new MudColor(0, 0.0, 0.0, 0.80),

                // Tables
                TableLines = new MudColor(0, 0.0, 0.93, 1.0),  // #EEEEEE
                TableStriped = new MudColor(0, 0.0, 0.0, 0.02),
                TableHover = new MudColor(0, 0.0, 0.0, 0.04),

                // Grays (string type)                          // hsl(0, 0%, L)
                GrayDefault = "#9E9E9E",                        // L=62%
                GrayLight = "#BDBDBD",                          // L=74%
                GrayLighter = "#EEEEEE",                        // L=93%
                GrayDark = "#757575",                           // L=46%
                GrayDarker = "#616161",                         // L=38%

                // Overlays (string type)
                OverlayLight = "rgba(255,255,255,0.50)",        // white 50%
                OverlayDark = "rgba(33,33,33,0.50)",            // ~L=13% 50%

                // Special (string type)
                Black = "#272c34",                              // hsl(215, 12%, 18%)

                // Opacity
                HoverOpacity = 0.06,
                RippleOpacity = 0.1,
                RippleOpacitySecondary = 0.2,
            },
            PaletteDark = new PaletteDark() {
                // Semantic colors                              // MudBlazor dark defaults
                Primary = new MudColor(183, 1.0, 0.45, 1.0),   // DIGN teal 
                PrimaryContrastText = new MudColor(0, 0.0, 0.0, 1.0), // black
                Info = new MudColor(212, 1.0, 0.60, 1.0),      // #3299FF
                Success = new MudColor(162, 0.90, 0.39, 1.0),  // #0BBA83
                Warning = new MudColor(40, 1.0, 0.50, 1.0),    // #FFA800
                Error = new MudColor(353, 0.90, 0.64, 1.0),    // #F64E62
                Dark = new MudColor(240, 0.07, 0.17, 1.0),     // #27272F

                // Text
                TextPrimary = new MudColor(0, 0.0, 1.0, 0.70),
                TextSecondary = new MudColor(0, 0.0, 1.0, 0.50),
                TextDisabled = new MudColor(0, 0.0, 1.0, 0.20),

                // Actions
                ActionDefault = new MudColor(240, 0.02, 0.68, 1.0), // #ADADB1
                ActionDisabled = new MudColor(0, 0.0, 1.0, 0.26),
                ActionDisabledBackground = new MudColor(0, 0.0, 1.0, 0.12),

                // Surfaces                                     // Sync: #32333D / #27272F in index*.html splash
                Background = new MudColor(60, 0.18, 0.04, 1.0), // hint of DIGN yellow // #32333D
                BackgroundGray = new MudColor(240, 0.07, 0.17, 1.0), // #27272F
                Surface = new MudColor(300, 0.14, 0.11, 1.0),  // hint of DIGN magenta

                // Drawer
                DrawerBackground = new MudColor(300, 0.16, 0.07, 1.0), // hint of DIGN magenta — #32333D
                DrawerText = new MudColor(0, 0.0, 1.0, 0.80),
                DrawerIcon = new MudColor(0, 0.0, 1.0, 0.80),

                // Appbar
                AppbarBackground = new MudColor(300, 0.06, 0.08, 1.0), // hint of DIGN magenta — #27272F
                AppbarText = new MudColor(0, 0.0, 1.0, 0.80),

                // Lines & borders
                LinesDefault = new MudColor(0, 0.0, 1.0, 0.12),
                LinesInputs = new MudColor(0, 0.0, 1.0, 0.30),
                Divider = new MudColor(0, 0.0, 1.0, 0.12),
                DividerLight = new MudColor(0, 0.0, 1.0, 0.06),

                // Tables
                TableLines = new MudColor(0, 0.0, 1.0, 0.12),
                TableStriped = new MudColor(0, 0.0, 1.0, 0.20),

                // Special
                // Black = new MudColor(240, 0.07, 0.17, 1.0),    // #27272F
            },
        };
    }
}

