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

        [JsonPropertyName("SelectedPrefix")]
        public string SelectedPrefix { get; init; } = String.Empty;

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
        public float InactivityTimeoutMinutes { get; init; } = AppConfig.IdleTimeoutMins;

        [JsonPropertyName("W_UserVerification")]
        public string UserVerification { get; init; } = AppConfig.DefaultUserVerification;

        [JsonPropertyName("W_ResidentKey")]
        public string ResidentKey { get; init; } = AppConfig.DefaultResidentKey;

        [JsonPropertyName("W_AuthenticatorAttachment")]
        public string AuthenticatorAttachment { get; init; } = AppConfig.DefaultAuthenticatorAttachment;

        [JsonPropertyName("W_Attestation")]
        public string Attestation { get; init; } = AppConfig.DefaultAttestation;

        [JsonPropertyName("W_SelectedTransportOptions")]
        public List<string> SelectedTransportOptions { get; init; } = AppConfig.DefaultAuthenticatorTransports;

        [JsonPropertyName("W_SelectedHints")]
        public List<string> SelectedHints { get; init; } = AppConfig.DefaultSelectedHints;
    }
}