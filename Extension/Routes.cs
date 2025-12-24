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
public enum ContentPage
{
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
public static class Routes
{
    public static readonly Dictionary<Type, PageRoute> Pages = new()
    {
        // Primary navigation (auth required)
        [typeof(DashboardPage)] = new("Dashboard", "/Dashboard.html", RequiresAuth: true,
            Icons.Material.Filled.Dashboard, Color.Surface),
        [typeof(IdentifiersPage)] = new("Identifiers", "/Identifiers.html", RequiresAuth: true,
            Icons.Material.Filled.Key, Color.Surface),
        [typeof(CredentialsPage)] = new("Credentials", "/Credentials.html", RequiresAuth: true,
            Icons.Material.Filled.Badge, Color.Surface),
        [typeof(WebsitesPage)] = new("Websites", "/Websites.html", RequiresAuth: true,
            Icons.Material.Filled.Web, Color.Surface),
        [typeof(Passkeys)] = new("Passkeys", "/Passkeys.html", RequiresAuth: true,
            Icons.Material.Filled.Key, Color.Surface),
        [typeof(KeriAgentServicePage)] = new("KERI Agent", "/KeriAgentService.html", RequiresAuth: true,
            Icons.Material.Outlined.PeopleOutline, Color.Surface),

        // Detail pages (auth required, no menu). Trailing / if route has parameter
        [typeof(IdentifierPage)] = new("Identifier", "/Identifier.html/", RequiresAuth: true),
        [typeof(WebsitePage)] = new("Website", "/Website.html/", RequiresAuth: true),
        [typeof(ConnectingPage)] = new("Connecting", "/Connecting.html", RequiresAuth: true),
        [typeof(RequestSignInPage)] = new("Request Sign In", "/RequestSignIn.html", RequiresAuth: true),
        [typeof(RequestSignHeadersPage)] = new("Request Sign Headers", "/RequestSignHeaders.html", RequiresAuth: true),
        [typeof(AddPasskeyPage)] = new("Add Passkey", "/AddPasskey.html", RequiresAuth: true),

        // Index pages (no auth) - Index.razor handles multiple routes
        [typeof(Index)] = new("Index", "/index.html", RequiresAuth: false),

        // Setup/config pages (no auth)
        [typeof(WelcomePage)] = new("Welcome", "/Welcome.html", RequiresAuth: false),
        [typeof(NewReleasePage)] = new("New Release", "/NewRelease.html", RequiresAuth: false),
        [typeof(ConfigurePage)] = new("Configure", "/Configure.html", RequiresAuth: false),
        [typeof(OfferPasskeyPage)] = new("Offer Passkey", "/OfferPasskey.html", RequiresAuth: false),
        [typeof(UnlockPage)] = new("Unlock", "/Unlock.html", RequiresAuth: false,
            Icons.Material.Filled.LockOpen, Color.Surface),
        [typeof(PreferencesPage)] = new("Preferences", "/ManagePreferences.html", RequiresAuth: false,
            Icons.Material.Filled.SettingsApplications, Color.Surface),
        [typeof(TermsPage)] = new("Terms", "/Terms.html", RequiresAuth: false),
        [typeof(PrivacyPage)] = new("Privacy", "/Privacy.html", RequiresAuth: false),
        [typeof(SidePanel)] = new("SidePanel", "/sidepanel.html", RequiresAuth: false),
        [typeof(DeletePage)] = new("Delete Config", "/Delete.html", RequiresAuth: false,
            Icons.Material.Filled.DeleteForever, Color.Surface),
        [typeof(TestPage)] = new("Test", "/Test.html", RequiresAuth: false,
            Icons.Material.Filled.TempleBuddhist, Color.Surface),
        [typeof(ReleaseHistoryPage)] = new("Release History", "/ReleaseHistory.html", RequiresAuth: false),
    };

    /// <summary>
    /// Additional paths that don't require auth but aren't separate page types.
    /// Index.razor handles multiple route paths.
    /// </summary>
    // TODO P2: could move this into some kind of attribute or comment on the PageRoute record, with enum names. See below IndexPaths, Content
    private static readonly HashSet<string> AdditionalPathsNotRequiringAuth =
    [
        "/indexInTab.html",
        "/indexInPopup.html",
        "/indexInSidePanel.html",
        "/indexInOptions.html",
    ];

    /// <summary>
    /// Context-specific Index page paths (all handled by Index.razor).
    /// </summary>
    public static class IndexPaths
    {
        public const string Default = "/index.html";
        public const string InTab = "/indexInTab.html";
        public const string InPopup = "/indexInPopup.html";
        public const string InSidePanel = "/indexInSidePanel.html";
        public const string InOptions = "/indexInOptions.html";
    }

    public static readonly Dictionary<ContentPage, ContentRoute> Content = new()
    {
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
        [ContentPage.ReleaseHistory] = new("Release History", "content/release_history.html",
            Icons.Material.Filled.StickyNote2, Color.Surface),
    };

    #region Page Helpers

    /// <summary>
    /// Check if a page type requires authentication.
    /// </summary>
    public static bool PageRequiresAuth<TPage>() where TPage : ComponentBase =>
        Pages
            .Where(p => p.Key == typeof(TPage))
            .Select(p => p.Value.RequiresAuth)
            .FirstOrDefault();

    /// <summary>
    /// Check if a page type requires authentication.
    /// </summary>
    public static bool PageRequiresAuth(Type pageType) =>
        Pages
            .Where(p => p.Key == pageType)
            .Select(p => p.Value.RequiresAuth)
            .FirstOrDefault();

    /// <summary>
    /// Check if a path requires authentication.
    /// </summary>
    public static bool PathRequiresAuth(string path) =>
        !Pages
            .Where(p => !p.Value.RequiresAuth)
            .Select(p => p.Value.Path)
            .Contains(path)
        && !AdditionalPathsNotRequiringAuth.Contains(path);

    /// <summary>
    /// Get the path for a page type.
    /// </summary>
    public static string PathFor<TPage>() where TPage : ComponentBase =>
        Pages
            .Where(p => p.Key == typeof(TPage))
            .Select(p => p.Value.Path)
            .FirstOrDefault()
        ?? throw new ArgumentException($"No route defined for {typeof(TPage).Name}");

    /// <summary>
    /// Get the path for a page type.
    /// </summary>
    public static string PathFor(Type pageType) =>
        Pages
            .Where(p => p.Key == pageType)
            .Select(p => p.Value.Path)
            .FirstOrDefault()
        ?? throw new ArgumentException($"No route defined for {pageType.Name}");

    /// <summary>
    /// Get the PageRoute for a page type.
    /// </summary>
    public static PageRoute For<TPage>() where TPage : ComponentBase =>
        Pages
            .Where(p => p.Key == typeof(TPage))
            .Select(p => p.Value)
            .FirstOrDefault();

    /// <summary>
    /// Get the PageRoute for a page type.
    /// </summary>
    public static PageRoute For(Type pageType) =>
        Pages
            .Where(p => p.Key == pageType)
            .Select(p => p.Value)
            .FirstOrDefault();

    /// <summary>
    /// Get all pages that require authentication.
    /// </summary>
    public static IEnumerable<PageRoute> PagesRequiringAuth() =>
        Pages
            .Where(p => p.Value.RequiresAuth)
            .Select(p => p.Value);

    /// <summary>
    /// Get all pages that do not require authentication.
    /// </summary>
    public static IEnumerable<PageRoute> PagesNotRequiringAuth() =>
        Pages
            .Where(p => !p.Value.RequiresAuth)
            .Select(p => p.Value);

    /// <summary>
    /// Get all pages that have menu icons (for menu rendering).
    /// </summary>
    public static IEnumerable<PageRoute> MenuPages() =>
        Pages
            .Where(p => p.Value.MenuIcon is not null)
            .Select(p => p.Value);

    /// <summary>
    /// Get all menu pages filtered by auth requirement.
    /// </summary>
    public static IEnumerable<PageRoute> MenuPages(bool requiresAuth) =>
        Pages
            .Where(p => p.Value.MenuIcon is not null && p.Value.RequiresAuth == requiresAuth)
            .Select(p => p.Value);

    #endregion

    #region Content Helpers

    /// <summary>
    /// Get the path for a content page.
    /// </summary>
    public static string PathFor(ContentPage contentPage) =>
        Content
            .Where(c => c.Key == contentPage)
            .Select(c => c.Value.Path)
            .FirstOrDefault()
        ?? throw new ArgumentException($"No route defined for {contentPage}");

    /// <summary>
    /// Get the ContentRoute for a content page.
    /// </summary>
    public static ContentRoute For(ContentPage contentPage) =>
        Content
            .Where(c => c.Key == contentPage)
            .Select(c => c.Value)
            .FirstOrDefault();

    /// <summary>
    /// Get all content pages that have menu icons (for menu rendering).
    /// </summary>
    public static IEnumerable<ContentRoute> MenuContent() =>
        Content
            .Where(c => c.Value.MenuIcon is not null)
            .Select(c => c.Value);

    #endregion
}
