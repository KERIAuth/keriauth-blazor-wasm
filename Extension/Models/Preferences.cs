namespace Extension.Models {
    using System.Text.Json.Serialization;
    using Extension.Models.Storage;

    public record Preferences : IStorageModel {

        [JsonPropertyName("IsDarkTheme")]
        public bool IsDarkTheme { get; init; } = AppConfig.DefaultIsDarkTheme;
        // Note: IsDarkTheme default should only be used here when initializing Preferences for the first time.
        // It may be randomly set by AppConfig for feature discoverability.

        /// <summary>
        /// When true, enables multi-KERIA configuration features throughout the app.
        /// </summary>
        [JsonPropertyName("IsMultiKeriaConfigEnabled")]
        public bool IsMultiKeriaConfigEnabled { get; init; } = true;

        /// <summary>
        /// When true (and IsMultiKeriaConfigEnabled is true), shows the KERIA selector on the Unlock page.
        /// </summary>
        [JsonPropertyName("IsMultiKeriaOnUnlock")]
        public bool IsMultiKeriaOnUnlock { get; init; }

        /// <summary>
        /// KERIA-specific preferences including selected connection and AID prefix.
        /// </summary>
        [JsonPropertyName("KeriaPreference")]
        public KeriaPreference KeriaPreference { get; init; } = new();

        /// <summary>
        /// Backward compatibility - reads SelectedPrefix from KeriaPreference.
        /// Note: SelectedPrefix is now stored per-config in KeriaConnectConfig.
        /// This property is kept for backward compatibility but should be accessed
        /// via AppCache.SelectedPrefix (which reads from the current config) instead.
        /// </summary>
        [JsonIgnore]
        [Obsolete("Use AppCache.SelectedPrefix instead, which reads from the current KeriaConnectConfig")]
        public string SelectedPrefix => KeriaPreference.SelectedPrefix;

        [JsonPropertyName("IsStored")]
        public bool IsStored { get; init; }

        [JsonPropertyName("DrawerVariantInPopup")]
        public MudBlazor.DrawerVariant DrawerVariantInPopup { get; init; } = MudBlazor.DrawerVariant.Temporary;

        [JsonPropertyName("DrawerVariantInTab")]
        public MudBlazor.DrawerVariant DrawerVariantInTab { get; init; } = AppConfig.DefaultDrawerVariantInTab;

        [JsonPropertyName("DrawerVariantInSidePanel")]
        public MudBlazor.DrawerVariant DrawerVariantInSidePanel { get; init; } = AppConfig.DefaultDrawerVariantInSidePanel;

        [JsonPropertyName("IsMenuOpenInTabOnStartup")]
        public bool IsMenuOpenInTabOnStartup { get; init; } = AppConfig.DefaultIsMenuOpenInTabOnStartup;

        [JsonPropertyName("IsMenuOpenInSidePanelOnStartup")]
        public bool IsMenuOpenInSidePanelOnStartup { get; init; } = AppConfig.DefaultIsMenuOpenInSidePanelOnStartup;

        [JsonPropertyName("IsPasskeyUsePreferred")]
        public bool IsPasskeyUsePreferred { get; init; } = AppConfig.DefaultIsPasskeyUsePreferred;

        [JsonPropertyName("IsSignRequestDetailShown")]
        public bool IsSignRequestDetailShown { get; init; }

        [JsonPropertyName("InactivityTimeoutMinutes")]
        public float InactivityTimeoutMinutes { get; init; } = AppConfig.DefaultInactivityTimeoutMins;

        [JsonPropertyName("SelectedViewDefIds")]
        public Dictionary<string, string> SelectedViewDefIds { get; init; } = [];

        [JsonPropertyName("BeepOnScanSuccess")]
        public bool BeepOnScanSuccess { get; init; } = AppConfig.DefaultBeepOnScanSuccess;

        [JsonPropertyName("PreferredCameraDeviceId")]
        public string? PreferredCameraDeviceId { get; init; }
    }
}
