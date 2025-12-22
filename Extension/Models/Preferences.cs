namespace Extension.Models {
    using Extension.Models.Storage;
    using System.Text.Json.Serialization;

    public record Preferences : IStorageModel {

        [JsonPropertyName("IsDarkTheme")]
        public bool IsDarkTheme { get; set; } = AppConfig.DefaultIsDarkTheme;

        [JsonPropertyName("SelectedPrefix")]
        public string SelectedPrefix { get; init; } = String.Empty;

        [JsonPropertyName("IsStored")]
        public bool IsStored { get; init; }

        [JsonPropertyName("DrawerVariantInPopup")]
        public MudBlazor.DrawerVariant DrawerVariantInPopup { get; init; } = MudBlazor.DrawerVariant.Temporary;

        [JsonPropertyName("DrawerVariantInTab")]
        public MudBlazor.DrawerVariant DrawerVariantInTab { get; init; } = AppConfig.DefaultDrawerVariantInTab;

        [JsonPropertyName("DrawerVariantInSidePanel")]
        public MudBlazor.DrawerVariant DrawerVariantInSidePanel { get; init; } = AppConfig.DefaultDrawerVariantInSidePanel;

        [JsonPropertyName("IsPersistentDrawerOpenInTab")]
        public bool IsPersistentDrawerOpenInTab { get; init; }

        [JsonPropertyName("IsPersistentDrawerOpenInSidePanel")]
        public bool IsPersistentDrawerOpenInSidePanel { get; init; }

        [JsonPropertyName("PrefersToUseAuthenticator")]
        public bool PrefersToUseAuthenticator { get; init; } = AppConfig.DefaultPrefersToUseAuthenticator;

        [JsonPropertyName("IsSignRequestDetailShown")]
        public bool IsSignRequestDetailShown { get; init; }

        [JsonPropertyName("InactivityTimeoutMinutes")]
        public float InactivityTimeoutMinutes { get; init; } = AppConfig.DefaultInactivityTimeoutMins;
    }
}
