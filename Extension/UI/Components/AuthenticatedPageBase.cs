using Extension.Services;
using Microsoft.AspNetCore.Components;

namespace Extension.UI.Components;

/// <summary>
/// Base class for pages that require authentication.
/// Prevents rendering when the user is not authenticated, avoiding null reference exceptions
/// during session timeout when AppCache data is cleared but navigation hasn't completed yet.
///
/// Usage:
/// <code>
/// @inherits AuthenticatedPageBase
/// @inject AppCache appCache
///
/// @code {
///     protected override async Task OnInitializedAsync() {
///         await base.OnInitializedAsync();
///         await this.SubscribeToAppCache(appCache);
///         // ... page-specific initialization
///     }
/// }
/// </code>
///
/// The page must inject AppCache and call InitializeAppCache() in OnInitializedAsync.
/// ShouldRender() will return false when not authenticated, preventing render errors.
/// </summary>
public abstract class AuthenticatedPageBase : ComponentBase {
    /// <summary>
    /// Reference to AppCache, must be set by derived class via InitializeAppCache() call.
    /// </summary>
    protected AppCache? AppCacheRef { get; private set; }

    /// <summary>
    /// Call this in OnInitializedAsync to enable authentication-based render suppression.
    /// </summary>
    /// <param name="appCache">The injected AppCache instance</param>
    protected void InitializeAppCache(AppCache appCache) {
        AppCacheRef = appCache;
    }

    /// <summary>
    /// Prevents rendering when not authenticated.
    /// This avoids null reference exceptions during session timeout when AppCache data is cleared
    /// but the page hasn't navigated away yet.
    /// </summary>
    protected override bool ShouldRender() {
        // If AppCache hasn't been initialized yet, allow render (for initial load)
        if (AppCacheRef is null) {
            return true;
        }
        // Only render when authenticated
        return AppCacheRef.IsAuthenticated;
    }
}
