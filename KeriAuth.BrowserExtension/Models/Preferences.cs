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

        [JsonPropertyName("iDT")]
        public bool IsDarkTheme { get; init; } = false;

        [JsonPropertyName("sAid")]
        public string SelectedAid { get; init; } = "";

        [JsonPropertyName("iOIDC")]
        public bool IsOptedIntoDataCollection { get; init; } = false;

        [JsonPropertyName("dvip")]
        public MudBlazor.DrawerVariant DrawerVariantInPopup { get; init; } = MudBlazor.DrawerVariant.Temporary;

        [JsonPropertyName("dvit")]
        public MudBlazor.DrawerVariant DrawerVariantInTab { get; init; } = MudBlazor.DrawerVariant.Temporary;

        [JsonPropertyName("dviw")]
        public string SelectedKeriaAlias { get; init; } = "";
    }
}