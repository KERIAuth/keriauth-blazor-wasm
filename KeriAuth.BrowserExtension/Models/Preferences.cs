namespace KeriAuth.BrowserExtension.Models
{
    using System.Text.Json.Serialization;

    public class Preferences
    {
        // TODO: create a constructor and make setters private

        [JsonPropertyName("iDT")]
        public bool IsDarkTheme { get; set; } = false;

        [JsonPropertyName("sAid")]
        public string SelectedAid { get; set; } = "";

        [JsonPropertyName("iOIDC")]
        public bool IsOptedIntoDataCollection { get; set; } = false;

        [JsonPropertyName("dvip")]
        public MudBlazor.DrawerVariant DrawerVariantInPopup { get; set; } = MudBlazor.DrawerVariant.Temporary;

        [JsonPropertyName("dvit")]
        public MudBlazor.DrawerVariant DrawerVariantInTab { get; set; } = MudBlazor.DrawerVariant.Temporary;

        [JsonPropertyName("dviw")]
        public string SelectedKeriaAlias { get; set; } = "";
    }
}