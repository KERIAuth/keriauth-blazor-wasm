namespace Extension;

using Extension.UI.Pages;
using Microsoft.AspNetCore.Components;
using MudBlazor;

/// <summary>
/// Represents a page route with metadata for navigation and authorization.
/// </summary>
public readonly record struct PageRoute(
    string Name,
    string Path,
    bool RequiresAuth,
    string? MenuIcon = null,
    Color? MenuIconColor = null
);

/// <summary>
/// Represents a static content route with metadata for navigation.
/// </summary>
public readonly record struct ContentRoute(
    string Name,
    string Path,
    string? MenuIcon = null,
    Color? MenuIconColor = null
);

/// <summary>
/// Identifies static content pages.
/// </summary>
public enum ContentPage {
    Terms,
    Privacy,
    About,
    Help,
    Licenses,
    Release,
    ReleaseHistory
}

/// <summary>
/// Centralized route definitions.
/// </summary>
public static class Routes {
    public static class Paths {
        public const string Dashboard = "/Dashboard.html";
        public const string Profiles = "/Profiles.html";
        public const string Connections = "/Connections.html";
        public const string Presentations = "/Presentations.html";
        public const string Credentials = "/Credentials.html";
        public const string Websites = "/Websites.html";
        public const string Notifications = "/Notifications.html";
        public const string Passkeys = "/Passkeys.html";
        public const string Mnemonic = "/Mnemonic.html";
        public const string Profile = "/Profile.html";
        public const string Website = "/Website.html";
        public const string Connecting = "/Connecting.html";
        public const string RequestSignIn = "/RequestSignIn.html";
        public const string RequestSignHeaders = "/RequestSignHeaders.html";
        public const string RequestSignData = "/RequestSignData.html";
        public const string RequestCreateCredential = "/RequestCreateCredential.html";
        public const string RequestConnect = "/RequestConnect.html";
        public const string RequestApproveIpex = "/RequestApproveIpex.html";
        public const string AddPasskey = "/AddPasskey.html";
        public const string Index = "/index.html";
        public const string Welcome = "/Welcome.html";
        public const string NewRelease = "/NewRelease.html";
        public const string Configure = "/Configure.html";
        public const string OfferPasskey = "/OfferPasskey.html";
        public const string GettingStarted = "/GettingStarted.html";
        public const string Unlock = "/Unlock.html";
        public const string Preferences = "/ManagePreferences.html";
        public const string KeriaConfigs = "/KeriaConfigs.html";
        public const string KeriaConfig = "/KeriaConfig.html";
        public const string KeriaHelp = "/KeriaHelp.html";
        public const string Delete = "/Delete.html";
        public const string Terms = "/Terms.html";
        public const string SidePanel = "/sidepanel.html";
        public const string DeveloperTest = "/DeveloperTest.html";
        public const string PrimeData = "/PrimeData.html";
        public const string Credential = "/Credential.html";

        public const string DeveloperState = "/DeveloperState.html";
        public const string ReleaseHistory = "/content/release_history.html";
    }
    public static class IndexPaths {
        public const string InTab = Paths.Index;
        public const string InPopup = Paths.Index + "?context=popup";
    }

    public static readonly Dictionary<Type, PageRoute> Pages = new() {
        // Primary navigation (auth required)
        [typeof(DashboardPage)] = new("Dashboard", Paths.Dashboard, RequiresAuth: true,
            Icons.Material.Filled.Dashboard, Color.Surface),
        [typeof(ProfilesPage)] = new("Profiles", Paths.Profiles, RequiresAuth: true,
            Icons.Material.Filled.Key, Color.Surface),
        [typeof(ConnectionsPage)] = new("Connections", Paths.Connections, RequiresAuth: true,
            Icons.Material.Filled.People, Color.Surface),
        [typeof(PresentationsPage)] = new("Presentations", Paths.Presentations, RequiresAuth: true,
            Icons.Material.Filled.PresentToAll, Color.Surface),
        [typeof(CredentialsPage)] = new("Credentials", Paths.Credentials, RequiresAuth: true,
            Icons.Material.Filled.Badge, Color.Surface),
        [typeof(WebsitesPage)] = new("Websites", Paths.Websites, RequiresAuth: true,
            Icons.Material.Filled.Web, Color.Surface),
        [typeof(NotificationsPage)] = new("Notifications", Paths.Notifications, RequiresAuth: true,
            Icons.Material.Filled.Notifications, Color.Surface),
        [typeof(Passkeys)] = new("Passkeys", Paths.Passkeys, RequiresAuth: true,
            Icons.Material.Filled.Key, Color.Surface),
        [typeof(MnemonicPage)] = new("Mnemonic", Paths.Mnemonic, RequiresAuth: true,
            Icons.Material.Filled.Key, Color.Surface),

        // Detail pages (auth required, no menu). Trailing / if route has parameter
        [typeof(ProfilePage)] = new("Profile", Paths.Profile, RequiresAuth: true),
        [typeof(WebsitePage)] = new("Website", Paths.Website, RequiresAuth: true),
        [typeof(ConnectingPage)] = new("Connecting", Paths.Connecting, RequiresAuth: true),
        [typeof(RequestSignInPage)] = new("Request Sign In", Paths.RequestSignIn, RequiresAuth: true),
        [typeof(RequestSignHeadersPage)] = new("Request Sign Headers", Paths.RequestSignHeaders, RequiresAuth: true),
        [typeof(RequestSignDataPage)] = new("Request Sign Data", Paths.RequestSignData, RequiresAuth: true),
        [typeof(RequestCreateCredentialPage)] = new("Request Create Credential", Paths.RequestCreateCredential, RequiresAuth: true),
        [typeof(RequestConnectPage)] = new("Request Connect", Paths.RequestConnect, RequiresAuth: true),
        [typeof(RequestApproveIpexPage)] = new("Request Approve IPEX", Paths.RequestApproveIpex, RequiresAuth: true),
        [typeof(AddPasskeyPage)] = new("Add Passkey", Paths.AddPasskey, RequiresAuth: true),

        // Index pages (no auth) - Index.razor handles multiple routes
        [typeof(Index)] = new("Index", Paths.Index, RequiresAuth: false),

        // Setup/config pages (no auth)
        [typeof(WelcomePage)] = new("Welcome", Paths.Welcome, RequiresAuth: false),
        [typeof(NewReleasePage)] = new("New Release", Paths.NewRelease, RequiresAuth: false),
        [typeof(ConfigurePage)] = new("Configure", Paths.Configure, RequiresAuth: false),
        [typeof(OfferPasskeyPage)] = new("Offer Passkey", Paths.OfferPasskey, RequiresAuth: false),
        [typeof(GettingStartedPage)] = new("Getting Started", Paths.GettingStarted, RequiresAuth: false),
        [typeof(UnlockPage)] = new("Unlock", Paths.Unlock, RequiresAuth: false,
            Icons.Material.Filled.LockOpen, Color.Surface),
        [typeof(PreferencesPage)] = new("Preferences", Paths.Preferences, RequiresAuth: false,
            Icons.Material.Filled.SettingsApplications, Color.Surface),
        [typeof(KeriaConfigsPage)] = new("KERIA Connections", Paths.KeriaConfigs, RequiresAuth: false,
            Icons.Material.Filled.Cloud, Color.Surface),
        [typeof(KeriaConfigPage)] = new("KERIA Connection", Paths.KeriaConfig, RequiresAuth: false),
        [typeof(KeriaHelpPage)] = new("About KERIA", Paths.KeriaHelp, RequiresAuth: false),
        [typeof(DeletePage)] = new("Delete", Paths.Delete, RequiresAuth: false,
            Icons.Material.Filled.DeleteForever, Color.Surface),
        [typeof(TermsPage)] = new("Terms", Paths.Terms, RequiresAuth: false),
        [typeof(SidePanel)] = new("SidePanel", Paths.SidePanel, RequiresAuth: false),
        [typeof(DeveloperTestPage)] = new("DeveloperTest", Paths.DeveloperTest, RequiresAuth: false,
            Icons.Material.Filled.TempleBuddhist, Color.Surface),
        [typeof(PrimeDataPage)] = new("PrimeData", Paths.PrimeData, RequiresAuth: true,
            Icons.Material.Filled.DataObject, Color.Surface),
        [typeof(CredentialPage)] = new("Credential", Paths.Credential, RequiresAuth: true),

        [typeof(DeveloperStatePage)] = new("DeveloperState", Paths.DeveloperState, RequiresAuth: false,
            Icons.Material.Filled.TempleBuddhist, Color.Surface),
    };

    /// <summary>
    /// Pages that use DialogLayout for handling pending BW→App requests.
    /// BaseLayout uses this to avoid re-navigating when already on a dialog page.
    /// </summary>
    public static readonly HashSet<string> DialogPagePaths =
    [
        Pages[typeof(RequestSignInPage)].Path,
        Pages[typeof(RequestSignHeadersPage)].Path,
        Pages[typeof(RequestSignDataPage)].Path,
        Pages[typeof(RequestCreateCredentialPage)].Path,
        Pages[typeof(RequestConnectPage)].Path,
        Pages[typeof(RequestApproveIpexPage)].Path,
    ];


    public static readonly Dictionary<ContentPage, ContentRoute> Content = new() {
        [ContentPage.Terms] = new("Terms", "content/terms.html",
            Icons.Material.Filled.StickyNote2, Color.Surface),
        [ContentPage.Privacy] = new("Privacy", "content/privacy.html",
            Icons.Material.Filled.StickyNote2, Color.Surface),
        [ContentPage.About] = new("About", "content/about.html",
            Icons.Material.Filled.Info, Color.Surface),
        [ContentPage.Help] = new("Help", "content/help.html",
            Icons.Material.Filled.Help, Color.Surface),
        [ContentPage.Licenses] = new("Licenses", "content/licenses.html",
            Icons.Material.Filled.StickyNote2, Color.Surface),
        [ContentPage.Release] = new("Release Notes", "content/release.html",
            Icons.Material.Filled.StickyNote2, Color.Surface),
        [ContentPage.ReleaseHistory] = new("Release History", Paths.ReleaseHistory,
            Icons.Material.Filled.StickyNote2, Color.Surface),
    };

    #region Page Helpers

    /// <summary>
    /// Check if a page type requires authentication.
    /// </summary>
    public static bool PageRequiresAuth<TPage>() where TPage : ComponentBase =>
        Pages.TryGetValue(typeof(TPage), out var route) && route.RequiresAuth;

    /// <summary>
    /// Check if a page type requires authentication.
    /// </summary>
    public static bool PageRequiresAuth(Type pageType) =>
        Pages.TryGetValue(pageType, out var route) && route.RequiresAuth;

    /// <summary>
    /// Check if a path is a dialog page (handles pending BW→App requests).
    /// </summary>
    public static bool IsDialogPage(string path) =>
        DialogPagePaths.Contains(path);

    /// <summary>
    /// Check if a path requires authentication.
    /// </summary>
    public static bool PathRequiresAuth(string path) =>
        !Pages
            .Where(p => !p.Value.RequiresAuth)
            .Select(p => p.Value.Path)
            .Contains(path);

    /// <summary>
    /// Get the path for a page type.
    /// </summary>
    public static string PathFor<TPage>() where TPage : ComponentBase =>
        Pages.TryGetValue(typeof(TPage), out var route)
            ? route.Path
            : throw new ArgumentException($"No route defined for {typeof(TPage).Name}");

    /// <summary>
    /// Get the path for a page type.
    /// </summary>
    public static string PathFor(Type pageType) =>
        Pages.TryGetValue(pageType, out var route)
            ? route.Path
            : throw new ArgumentException($"No route defined for {pageType.Name}");

    /// <summary>
    /// Get the PageRoute for a page type.
    /// </summary>
    public static PageRoute For<TPage>() where TPage : ComponentBase =>
        Pages.TryGetValue(typeof(TPage), out var route) ? route : default;

    /// <summary>
    /// Get the PageRoute for a page type.
    /// </summary>
    public static PageRoute For(Type pageType) =>
        Pages.TryGetValue(pageType, out var route) ? route : default;

    /// <summary>
    /// Get all pages that require authentication.
    /// </summary>
    public static List<PageRoute> PagesRequiringAuth() =>
        Pages
            .Where(p => p.Value.RequiresAuth)
            .Select(p => p.Value)
            .ToList();

    /// <summary>
    /// Get all pages that do not require authentication.
    /// </summary>
    public static List<PageRoute> PagesNotRequiringAuth() =>
        Pages
            .Where(p => !p.Value.RequiresAuth)
            .Select(p => p.Value)
            .ToList();

    /// <summary>
    /// Get all pages that have menu icons (for menu rendering).
    /// </summary>
    public static List<PageRoute> MenuPages() =>
        Pages
            .Where(p => p.Value.MenuIcon is not null)
            .Select(p => p.Value)
            .ToList();

    /// <summary>
    /// Get all menu pages filtered by auth requirement.
    /// </summary>
    public static List<PageRoute> MenuPages(bool requiresAuth) =>
        Pages
            .Where(p => p.Value.MenuIcon is not null && p.Value.RequiresAuth == requiresAuth)
            .Select(p => p.Value)
            .ToList();

    #endregion

    #region Content Helpers

    /// <summary>
    /// Get the path for a content page.
    /// </summary>
    public static string PathFor(ContentPage contentPage) =>
        Content.TryGetValue(contentPage, out var route)
            ? route.Path
            : throw new ArgumentException($"No route defined for {contentPage}");

    /// <summary>
    /// Get the ContentRoute for a content page.
    /// </summary>
    public static ContentRoute For(ContentPage contentPage) =>
        Content.TryGetValue(contentPage, out var route) ? route : default;

    /// <summary>
    /// Get all content pages that have menu icons (for menu rendering).
    /// </summary>
    public static List<ContentRoute> MenuContent() =>
        Content
            .Where(c => c.Value.MenuIcon is not null)
            .Select(c => c.Value)
            .ToList();

    #endregion
}
