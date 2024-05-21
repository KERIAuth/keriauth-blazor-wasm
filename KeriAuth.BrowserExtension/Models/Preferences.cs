namespace KeriAuth.BrowserExtension.Models
{
    using System.Text.Json.Serialization;

    public class Preferences
    {
        [JsonPropertyName("iDT")]
        public bool IsDarkTheme { get; set; } = false;

        [JsonPropertyName("sDid")]
        public string SelectedDidIdentifier { get; set; } = "";

        [JsonPropertyName("iOIDC")]
        public bool IsOptedIntoDataCollection { get; set; } = false;

        [JsonPropertyName("dvip")]
        public MudBlazor.DrawerVariant DrawerVariantInPopup { get; set; } = MudBlazor.DrawerVariant.Temporary;

        [JsonPropertyName("dvit")]
        public MudBlazor.DrawerVariant DrawerVariantInTab { get; set; } = MudBlazor.DrawerVariant.Temporary;
    }
}