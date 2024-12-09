using MudBlazor;
using MudBlazor.Utilities;

namespace KeriAuth.BrowserExtension
{
    public static class AppConfig
    {
        // Routes
        // TODO P4 These must match the fixed @page string values. Could write unit tests to confirm consistency, but can't have a correct-by-construction consistency :-(
        public const string RouteToIdentifiers = "/Identifiers";
        public const string RouteToCredentials = "/Credentials";
        public const string RouteToWebsites = "/Websites";
        public const string RouteToIndex = "/";
        public const string RouteToDelete = "/Delete";
        public const string RouteToNewInstall = "/NewInstall";
        public const string RouteToHome = "/Home";
        public const string RouteToTermsPage = "/Terms";
        public const string RouteToManagePrefs = "/ManagePreferences";
        public const string RouteToManageAgents = "/KeriAgentService";
        public const string RouteToWelcome = "/Welcome";
        public const string RouteToNewRelease = "/NewRelease";
        public const string RouteToConfigure = "/Configure";
        public const string RouteToAddAuthenticator = "/AddAuthenticator";
        public const string RouteToAuthenticators = "/Authenticators";
        public const string RouteToUnlock = "/Unlock";
        // TODO P2 decide whether a connecting page is needed
        public const string RouteToConnecting = "/Connecting";

        // route with parameters, with intentional trailing /
        public const string RouteToIdentifier = "/Identifier/";
        public const string RouteToWebsite = "/Website/";
        public const string RouteToRequestSignIn = "/RequestSignIn/";
        public const string RouteToRequestSign = "/RequestSign/";

        // routes to static content
        public const string RouteToTermsHtml = "content/terms.html";
        public const string RouteToPrivacyHtml = "content/privacy.html";
        public const string RouteToAbout = "content/about.html";
        public const string RouteToHelp = "content/help.html";
        public const string RouteToLicensesHtml = "content/licenses.html";
        public const string RouteToReleaseHtml = "content/release.html";

        // Terms Of Service and Privacy Policy hashes for the current release
        public const double TosHash = 1470787834;
        public const float PrivacyHash = 1251669842;

        // TODO P2 view on UnlockPage should use what is set in preferences
        public const float IdleTimeoutMins = 5f;
        public const string DefaultKeriaConnectAlias = "localhost";
        public const string DefaultKeriaAdminUrl = "http://localhost:3901";
        public const string DefaultKeriaBootUrl = "http://localhost:3903";
        public const int SignifyTimeoutMs = 10000; // Note, had fast retry issues when this was set to 1000.

        public const string ExampleAlias = "e.g. Maria Garcia as Compliance Analyst at Prime Industries";

        public static readonly List<string> ViewsNotRequiringAuth =
        [
            RouteToIndex,
            RouteToDelete,
            RouteToNewInstall,
            RouteToWelcome,
            RouteToNewRelease,
            RouteToConfigure,
            RouteToUnlock,
            RouteToManagePrefs,
            RouteToTermsPage
            // Note, you don't need to add routes to static html (non-SPA-App) here
        ];

        public static readonly MudTheme MyCustomTheme = new()
        {
            // See also https://mudblazor.com/customization/default-theme
            PaletteLight = new PaletteLight()
            {
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
            PaletteDark = new PaletteDark()
            {
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

