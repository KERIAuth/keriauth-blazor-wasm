namespace KeriAuth.BrowserExtension.Models
{
    using System.Text.Json.Serialization;

    public record Preferences
    {
        public Preferences()
        {
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

        [JsonPropertyName("ShowSignRequestDetail")]
        public bool ShowSignRequestDetail { get; init; }
    }
}