﻿using KeriAuth.BrowserExtension.Services.SignifyService.Models;
using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    [method: JsonConstructor]
    public class WalletCleartext(Preferences preferencesConfig, KeriaConfiguration KeriaConfigs, List<Identifier> identifiers, List<Credential> credentials, List<Website> websites, List<WebsiteInteractionThread> websiteInteractions)
    {
        [JsonPropertyName("prefs")]
        public Preferences Prefs { get; } = preferencesConfig;

        [JsonPropertyName("keria")]
        public KeriaConfiguration KeriaConfig { get; } = KeriaConfigs;

        [JsonPropertyName("ids")]
        public List<Identifier> Identifiers { get; } = identifiers;

        [JsonPropertyName("creds")]
        public List<Credential> Credentials { get; } = credentials;

        [JsonPropertyName("websites")]
        public List<Website> Websites { get; } = websites;

        [JsonPropertyName("wis")]
        public List<WebsiteInteractionThread> WebsiteInteractions { get; } = websiteInteractions;

        [JsonPropertyName("tcUTC")]
        public DateTime TimeCreatedUtc { get; } = DateTime.UtcNow;
    }
}
