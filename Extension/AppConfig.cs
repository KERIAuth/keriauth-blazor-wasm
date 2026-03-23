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
        public const double NotificationPollAlarmPeriodMinutes = 5.0;
        public static readonly TimeSpan NotificationPollInterval = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan NotificationBurstDuration = TimeSpan.FromSeconds(120);

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
            new KeriaPreset($"{ProductName} Test Cloud", "https://keria.cloud.dign.id", "https://keria-boot.cloud.dign.id"),
            // TODO P2: add basic auth for agent Boot
            // Note: GLEIF Test Cloud boot is :3903; however, that requires a HTTP Basic Auth request, and we don't yet support sending a Base64-encoded username (empty) and password, e.g. "Authorization: Basic dXNlcjpwYXNzd29yZA=="
            // new KeriaPreset("GLEIF Test Cloud", "https://keria.testnet.gleif.org:3901", ""),
            // new KeriaPreset("Veridian Dev Testnet", "https://keria.dev.idw-sandboxes.cf-deployments.org", "https://keria-boot.dev.idw-sandboxes.cf-deployments.org"),
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
            IsStored = false,
            KeriaPreference = new KeriaPreference()
        };

        // Other settings
        public const int DisplaySessionExpirationAtSecondsRemaining = 30;
        // Display inactivity countdown on AppBar when this many seconds are remaining
        public const int AppBarRefreshTimerDueTimeSeconds = 20;
        public static readonly TimeSpan ThrottleInactivityInterval = TimeSpan.FromSeconds(10);
        public const int ExpectedTermsDigest = 676316907;
        public const int ExpectedPrivacyDigest = 607994721;

        // Branding
        public const string ProductName = "DIGN";
        public const string FullProductName = "DIGN Identity Wallet";
        public const string LogPrefix = "dign";
        public const string WebsiteUrl = "https://dign.id";
        public const string UninstallUrl = "https://dign.id/uninstall.html";

        public static readonly MudTheme MyCustomTheme = new() {
            // See also https://mudblazor.com/customization/default-theme
            PaletteLight = new PaletteLight() {
                Primary = new MudColor(201, 1.0, 0.38, 1.0), // Colors.Indigo.Default,
                PrimaryLighten = Colors.Indigo.Lighten1,
                PrimaryDarken = Colors.Indigo.Darken1,
                PrimaryContrastText = Colors.Gray.Lighten5,
                // TextPrimary **

                Secondary = Colors.Brown.Default,
                SecondaryLighten = Colors.Brown.Lighten1,
                SecondaryDarken = Colors.Brown.Darken1,
                SecondaryContrastText = Colors.Gray.Lighten5,
                // TextSecondary  **

                Tertiary = Colors.DeepOrange.Default,
                TertiaryLighten = Colors.DeepOrange.Lighten1,
                TertiaryDarken = Colors.DeepOrange.Darken1,
                TertiaryContrastText = Colors.Gray.Lighten5,

                // Info
                // InfoLighten
                // InfoDarken
                // InfoContrastText

                // Success
                // SuccessLighten
                // SuccessDarken
                // SuccessContrastText
                // TextSuccess

                // Warning,
                // WarningLighten,
                // WarningDarken,
                // WarningContrastText,

                // Error,
                // ErrorDarken,
                // ErrorLighten,
                // ErrorContrastText,

                // Dark
                // DarkLighten
                // DarkDarken
                // DarkContrastText

                // Sync: hex equivalent #f5f5f5 is used in index*.html splash theme script
                Background = new MudColor(0, 0.0, 0.96, 1.0),
                BackgroundGray = new MudColor(0, 0.0, 0.76, 1.0),

                AppbarBackground = new MudColor(200, 0.17, 0.26, 1.0),
                // AppbarText

                TextDisabled = new MudColor(0, 0.0, 0.53, 1.0),

                DrawerBackground = new MudColor(0.0, 1.0, 0.10, 1.0),
                DrawerIcon = new MudColor(0, 1.0, 1.0, 1.0),
                DrawerText = new MudColor(0, 1.0, 1.0, 1.0),

                ActionDisabled = Colors.Gray.Default,

                Surface = new MudColor(0, 0.0, 0.90, 1.0),
                /*
                White
                Black

                TableStriped
                TableLines
                TableHover

                Surface

                OverlayLight
                OverlayDark

                LinesInputs
                LinesDefault

                HoverOpacity

                GrayLighter
                GrayLight
                GrayDefault
                GrayDarker
                GrayDark

                Divider
                DividerLight
                DividerDark

                ActionDisabledBackground
                ActionDisabled
                ActionDefault
                */
            },
            PaletteDark = new PaletteDark() {
                Primary = Colors.LightBlue.Lighten4,
                PrimaryLighten = Colors.LightBlue.Lighten3,
                PrimaryDarken = Colors.Cyan.Lighten5,
                PrimaryContrastText = Colors.Gray.Darken4,

                Secondary = Colors.Amber.Lighten4,
                SecondaryLighten = Colors.Amber.Lighten3,
                SecondaryDarken = Colors.Amber.Lighten5,
                SecondaryContrastText = Colors.Gray.Darken4,

                Tertiary = Colors.DeepOrange.Lighten4,
                TertiaryLighten = Colors.DeepOrange.Lighten3,
                TertiaryDarken = Colors.DeepOrange.Lighten5,
                TertiaryContrastText = Colors.Gray.Darken4,

                ActionDisabled = Colors.Gray.Darken1,
                ActionDisabledBackground = Colors.Gray.Default,

                // Sync: hex equivalent #182126 is used in index*.html splash theme script
                Background = new MudColor(201, 0.23, 0.12, 1.0),
                BackgroundGray = new MudColor(0, 0.0, 0.13, 1.0),
                Success = new MudColor(123, 0.41, 0.45, 1.0),
                // Error = new MudColor(244, 0.67, 0.54, 1.0),
                AppbarBackground = new MudColor(200, 0.19, 0.18, 1.0),
                TextPrimary = new MudColor(0, 0.0, 0.92, 1.0),

                TextSecondary = new MudColor(0, 0.0, 0.45, 1.0),
                Surface = new MudColor(0, 0.0, 0.21, 1.0),
                LinesDefault = new MudColor(0, 0.0, 1.0, 0.12),

                DrawerText = new MudColor(0, 0.0, 1.0, 1.0),
                TextDisabled = new MudColor(0, 0.0, 0.53, 1.0),
                DrawerBackground = new MudColor(200, 0.19, 0.18, 1.0),
                DrawerIcon = new MudColor(0, 1.0, 1.0, 1.0),
            },
        };
    }
}

