using MudBlazor;
using MudBlazor.Utilities;

namespace Extension {
    public static class AppConfig {
        // Note there is also a default InactivityTimeout in servicw-worker.ts that should be overridden by this during app startup
        public const int SignifyTimeoutMs = 20000; // Note, had fast retry issues when this was set to 1000, since a KERIA boot with KERIA oobi behinds the scenes take long time.

        // SessionManager configuration
        public const string SessionManagerAlarmName = "SessionManagerAlarm";

        // Request/Response messaging configuration
        public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);

        // KERIA connection settings
        public const string LocalhostKeriaConnectAlias = "localhost";
        public const string LocalhostKeriaAdminUrl = "http://localhost:3901";
        public const string LocalhostKeriaBootUrl = "http://localhost:3903";

        // default Preferences
        public const float DefaultInactivityTimeoutMins = 20.0f; // TODO P2 should be 10.0f;
        public const float MaxInactivityTimeoutMins = 20.0f; // TODO P2 should be 10.0f;
        // Intentionally random initial IsDarkTheme to improve discoverability
        public static bool DefaultIsDarkTheme => Random.Shared.Next(2) == 0; // false;
        public const MudBlazor.DrawerVariant DefaultDrawerVariantInTab = MudBlazor.DrawerVariant.Persistent;
        public const MudBlazor.DrawerVariant DefaultDrawerVariantInSidePanel = MudBlazor.DrawerVariant.Temporary;
        public const bool DefaultIsPasskeyUsePreferred = true;
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
            IsPersistentDrawerOpenInSidePanel = false,
            IsPersistentDrawerOpenInTab = false,
            IsSignRequestDetailShown = false,
            IsStored = false,
            SelectedPrefix = String.Empty
        };

        // Other settings
        public const int DisplaySessionExpirationAtSecondsRemaining = 30;
        public static readonly TimeSpan ThrottleInactivityInterval = TimeSpan.FromSeconds(10);
        public const int ExpectedTermsDigest = -497501087;
        public const int ExpectedPrivacyDigest = 529864982;

        // Branding and localization
        public const string ExampleAlias = "e.g. Maria Garcia, Compliance Analyst at Prime Industries";
        public const string ProductName = "KERI Auth";

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

                Background = new MudColor(0, 0.0, 0.96, 1.0),
                BackgroundGray = new MudColor(0, 0.0, 0.76, 1.0),

                AppbarBackground = new MudColor(200, 0.17, 0.26, 1.0),
                // AppbarText

                TextDisabled = new MudColor(0, 0.0, 0.53, 1.0),

                DrawerBackground = new MudColor(0, 100, 10, 1.0),
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

