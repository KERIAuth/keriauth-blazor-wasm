namespace KeriAuth.BrowserExtension.Models
{
    using System.Text.Json.Serialization;

    public readonly struct Preferences
    {
        public Preferences(bool isDarkTheme, string selectedAid, bool isOptedIntoDataCollection, MudBlazor.DrawerVariant drawerVariantInPopup, MudBlazor.DrawerVariant drawerVariantInTab, string selectedKeriaAlias)
        {
            IsDarkTheme = isDarkTheme;
            SelectedAid = selectedAid;
            IsOptedIntoDataCollection = isOptedIntoDataCollection;
            DrawerVariantInPopup = drawerVariantInPopup;
            DrawerVariantInTab = drawerVariantInTab;
            SelectedKeriaAlias = selectedKeriaAlias;
        }

        [JsonPropertyName("IsDarkTheme")]
        public bool IsDarkTheme { get; init; } = false;

        [JsonPropertyName("SelectedAid")]
        public string SelectedAid { get; init; } = "";

        [JsonPropertyName("IsOptedIntoDataCollection")]
        public bool IsOptedIntoDataCollection { get; init; } = false;

        [JsonPropertyName("DrawerVariantInPopup")]
        public MudBlazor.DrawerVariant DrawerVariantInPopup { get; init; } = MudBlazor.DrawerVariant.Temporary;

        [JsonPropertyName("DrawerVariantInTab")]
        public MudBlazor.DrawerVariant DrawerVariantInTab { get; init; } = MudBlazor.DrawerVariant.Temporary;

        [JsonPropertyName("SelectedKeriaAlias")]
        public string SelectedKeriaAlias { get; init; } = "";
    }
}