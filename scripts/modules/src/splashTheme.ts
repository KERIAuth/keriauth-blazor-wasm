/**
 * Applies theme-appropriate background color to the splash screen before Blazor renders.
 * Reads IsDarkTheme from chrome.storage.local ("Preferences" key) and sets CSS custom
 * properties --splash-bg and --splash-fg on the document root.
 *
 * Colors must be kept in sync with AppConfig.cs:
 *   PaletteLight.Background = hsl(0, 0%, 96%)   → #f5f5f5
 *   PaletteDark.Background  = hsl(201, 23%, 12%) → #182126
 */

const LIGHT_BG = '#f5f5f5';
const DARK_BG = '#182126';
const LIGHT_FG = '#212121';
const DARK_FG = '#ebebeb';

(async () => {
    try {
        const r = await chrome.storage.local.get('Preferences');
        const isDark = r?.Preferences?.IsDarkTheme ?? false;
        document.documentElement.style.setProperty('--splash-bg', isDark ? DARK_BG : LIGHT_BG);
        document.documentElement.style.setProperty('--splash-fg', isDark ? DARK_FG : LIGHT_FG);
    } catch (e) {
        // First run or error: CSS fallback values in the HTML apply
    }
})();
