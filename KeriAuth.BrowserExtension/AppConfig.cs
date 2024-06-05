namespace KeriAuth.BrowserExtension
{
    public static class AppConfig
    {
        // Routes
        public const string RouteToIdentifiers = "/Identifiers";
        public const string RouteToIdentifier = "/Identifier";  // Add parameter
        public const string RouteToCredentials = "/Credentials";
        public const string RouteToCredential = "/Credential";  // Add parameter
        public const string RouteToWebsites = "/Websites";
        public const string RouteToWebsite = "/Website";  // Add parameter
        public const string RouteToContacts = "/Contacts";
        public const string RouteToStart = "/Start";
        public const string RouteToDelete = "/Delete";
        public const string RouteToChat = "/Chat";
        public const string RouteToNewInstall = "/NewInstall";
        public const string RouteToReleaseHistory = "/ReleaseHistory";
        public const string RouteToHome = "/Home";
        public const string RouteToWalletRequests = "/WalletRequests";
        public const string RouteToWalletRequestsWithId = "/WalletRequests/{id}";
        public const string RouteToIndex = "/index.html";
        public const string RouteToManagePrefs = "/ManagePreferences";
        public const string RouteToManageMediators = "/Mediators";
        public const string RouteToBackup = "/Backup";
        public const string RouteToTerms = "content/terms.html";
        public const string RouteToPrivacy = "content/privacy.html";
        public const string RouteToAbout = "content/about.html";
        public const string RouteToHelp = "content/help.html";
        public const string RouteToLicenses = "content/licenses.html";
        public const string RouteToRelease = "content/release.html";
        public const string RouteToGroups = "/Groups";
        public const string RouteToNotifications = "/Notifications";
        public const string RouteToSchemas = "/Schemas";
        public const string RouteToManageAgents = "/Agents";
        public const string RouteToWelcomeView = "/Welcome";
        public const string RouteToNewReleaseView = "/NewRelease";
        public const string RouteToTermsView = "/Terms";
        public const string RouteToConfigureView = "/Configure";
        public const string RouteToUnlockView = "/Unlock";
        public const string RouteToConnectingView = "/Connecting";


        // Idle Timeout
        public const int IdleDebounceTimeSpanSecs = 5;
#if DEBUG
        public const int IdleTimeoutTimeSpanSecs = 300; // don't usually timeout in debug mode except when testing
#else
        public const int IdleTimeoutTimeSpanSecs = 300;  // 300s = 5m
#endif
        public static int IdleTimeoutTimeSpanMins => (int)Math.Round((Double)(IdleTimeoutTimeSpanSecs / 60), MidpointRounding.AwayFromZero);
        public const string DefaultKeriaConnectAlias = "localhost";
        public const string DefaultKeriaAdminUrl = "http://localhost:3901";
        public const string DefaultKeriaBootUrl = "http://localhost:3903";
        public const int SignifyTimeoutMs = 6000;
    }
}

