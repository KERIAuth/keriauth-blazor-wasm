namespace Extension.Models {
    using System.Text.Json.Serialization;
    using Extension.Models.Storage;

    public record Preferences : IVersionedStorageModel {

        [JsonPropertyName("SchemaVersion")]
        public int SchemaVersion { get; init; } = 2;

        [JsonPropertyName("SelectedKeriaConnectionDigest")]
        public string? SelectedKeriaConnectionDigest { get; init; }

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

        [JsonPropertyName("CredentialsDetailLevel")]
        public int CredentialsDetailLevel { get; init; } = (int)CredentialDetailLevel.WithOptionalSections;

        [JsonPropertyName("CredentialsDisplayType")]
        public CredentialDisplayType CredentialsDisplayType { get; init; } = CredentialDisplayType.Tree;

        /// <summary>
        /// Diagnostic flag. When true, TypeScript modules emit console.debug messages.
        /// When false (default), the consoleDebugGate.ts module replaces console.debug with a no-op
        /// in every extension context (BW, App, content script). The chrome.storage.onChanged listener
        /// reflects toggles live without restart.
        /// </summary>
        [JsonPropertyName("IsConsoleDebugLogged")]
        public bool IsConsoleDebugLogged { get; init; }

        /// <summary>
        /// When true, SAID values in the credential view render in abbreviated form
        /// (head+tail with an ellipsis) via the <c>SaidDisplay</c> component. When false,
        /// the full SAID is shown. Default true for visual density.
        /// </summary>
        [JsonPropertyName("AbbreviateSaids")]
        public bool AbbreviateSaids { get; init; } = true;
    }
}
