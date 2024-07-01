using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace KeriAuth.BrowserExtension.Helper
{
    // Maintainers: need to keep the imported method and property names aligned with UIHelper.ts
    [SupportedOSPlatform("browser")]
    public static partial class UIHelper
    {
        [JSImport("Utils.bt_scrollToItem", "uiHelper")]
        internal static partial string Bt_scrollToItem(string elementId);

        [JSImport("Utils.closeCurrentTab", "uiHelper")]
        internal static partial void CloseCurrentTab();

        [JSImport("Utils.newTabAndClosePopup", "uiHelper")]
        internal static partial Task NewTabAndClosePopup();

        [JSImport("Utils.createTab", "uiHelper")]
        internal static partial void CreateTab(string urlString);

        [JSImport("Utils.copy2Clipboard", "uiHelper")]
        internal static partial Task Copy2Clipboard(string text);

        [JSImport("Utils.restartBlazorApp", "uiHelper")]
        internal static partial void RestartBlazorApp(string urlString);

        [JSImport("Utils.getChromeContexts", "uiHelper")]
        internal static partial Task<string> GetChromeContexts();

        [JSImport("Utils.closeWindow", "uiHelper")]
        internal static partial void CloseWindow();
    }
}