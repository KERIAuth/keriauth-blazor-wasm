namespace KeriAuth.BrowserExtension.Models
{
    using System.Text.Json.Serialization;

    public record Preferences
    {
        public Preferences()
        {
        }

        [JsonPropertyName("IsDarkTheme")]
        public bool IsDarkTheme { get; init; }

        [JsonPropertyName("SelectedAid")]
        public string SelectedAid { get; init; } = String.Empty;

        [JsonPropertyName("IsOptedIntoDataCollection")]
        public bool IsOptedIntoDataCollection { get; init; }

        [JsonPropertyName("DrawerVariantInPopup")]
        public MudBlazor.DrawerVariant DrawerVariantInPopup { get; init; } = MudBlazor.DrawerVariant.Temporary;

        [JsonPropertyName("DrawerVariantInTab")]
        public MudBlazor.DrawerVariant DrawerVariantInTab { get; init; } = MudBlazor.DrawerVariant.Persistent;

        [JsonPropertyName("IsPersistentDrawerOpen")]
        public bool IsPersistentDrawerOpen { get; init; }

        [JsonPropertyName("SelectedKeriaAlias")]
        public string SelectedKeriaAlias { get; init; } = String.Empty;

        [JsonPropertyName("ShowSignRequestDetail")]
        public bool ShowSignRequestDetail { get; init; }

        [JsonPropertyName("InactivityTimeoutMinutes")]
        public float InactivityTimeoutMinutes { get; init; } = 1f;
    }
}