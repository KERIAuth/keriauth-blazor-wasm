namespace Extension.Models {
    using Extension.Models.Storage;
    using System.Text.Json.Serialization;

    public record Preferences : IStorageModel {

        [JsonPropertyName("IsDarkTheme")]
        public bool IsDarkTheme { get; set; } = AppConfig.DefaultIsDarkTheme;

        [JsonPropertyName("SelectedPrefix")]
        public string SelectedPrefix { get; init; } = String.Empty;

        [JsonPropertyName("IsOptedIntoDataCollection")]
        public bool IsOptedIntoDataCollection { get; init; } = false;

        [JsonPropertyName("DrawerVariantInPopup")]
        public MudBlazor.DrawerVariant DrawerVariantInPopup { get; init; } = MudBlazor.DrawerVariant.Temporary;

        [JsonPropertyName("DrawerVariantInTab")]
        public MudBlazor.DrawerVariant DrawerVariantInTab { get; init; } = AppConfig.DefaultDrawerVariantInTab;

        [JsonPropertyName("IsPersistentDrawerOpen")]
        public bool IsPersistentDrawerOpen { get; init; }

        [JsonPropertyName("PrefersToUseAuthenticator")]
        public bool PrefersToUseAuthenticator { get; init; } = AppConfig.DefaultPrefersToUseAuthenticator;

        [JsonPropertyName("SelectedKeriaAlias")]
        public string SelectedKeriaAlias { get; init; } = String.Empty;

        [JsonPropertyName("ShowSignRequestDetail")]
        public bool ShowSignRequestDetail { get; init; }

        [JsonPropertyName("InactivityTimeoutMinutes")]
        public float InactivityTimeoutMinutes { get; init; } = AppConfig.DefaultInactivityTimeoutMins;

        [JsonPropertyName("W_UserVerification")]
        public string UserVerification { get; init; } = AppConfig.DefaultUserVerification;

        [JsonPropertyName("W_ResidentKey")]
        public string ResidentKey { get; init; } = AppConfig.DefaultResidentKey;

        [JsonPropertyName("W_AuthenticatorAttachment")]
        public string AuthenticatorAttachment { get; init; } = AppConfig.DefaultAuthenticatorAttachment;

        [JsonPropertyName("W_Attestation")]
        public string AttestationConveyancePref { get; init; } = AppConfig.DefaultAttestationConveyancePref;

        [JsonPropertyName("W_SelectedTransportOptions")]
        public List<string> SelectedTransportOptions { get; init; } = AppConfig.DefaultAuthenticatorTransports;

        [JsonPropertyName("W_SelectedHints")]
        public List<string> SelectedHints { get; init; } = AppConfig.DefaultSelectedHints;
    }
}
