﻿using System.Text.Json.Serialization;
namespace KeriAuth.BrowserExtension.Models
{
    public record Website
    {
        public Website(
        Uri origin,
        List<WebsiteInteraction> interactions,
        string? rememberedPrefixOrNothing,
        string? rememberedCredSaidOrNothing,
        AutoSignInMode autoSignInChoice)
        {
            Origin = origin;
            Interactions = interactions;
            RememberedPrefixOrNothing = rememberedPrefixOrNothing;
            RememberedCredSaidOrNothing = rememberedCredSaidOrNothing;
            AutoSignInChoice = autoSignInChoice;
        }

        [JsonPropertyName("origin")]
        public Uri Origin { get; init; }

        [JsonPropertyName("websiteInteractions")]
        public List<WebsiteInteraction> Interactions { get; init; }

        [JsonPropertyName("rememberedPrefixOrNothing")]
        public string? RememberedPrefixOrNothing { get; init; }

        [JsonPropertyName("rememberedCredSaidOrNothing")]
        public string? RememberedCredSaidOrNothing { get; init; }

        [JsonPropertyName("autoSignInChoice")]
        public AutoSignInMode AutoSignInChoice
        {
            get => _autoSignInMode;
            init
            {
                _autoSignInMode = value switch
                {
                    AutoSignInMode.Identifier when RememberedPrefixOrNothing is not null => AutoSignInMode.Identifier,
                    AutoSignInMode.Credential when RememberedCredSaidOrNothing is not null => AutoSignInMode.Credential,
                    _ => AutoSignInMode.None,
                };
            }
        }

        private AutoSignInMode _autoSignInMode;
    }

    public enum AutoSignInMode
    {
        None = 1,
        Identifier = 2,
        Credential = 3
    }

    public record WebsiteInteraction
    {
        // TODO: Implement this record
        // RequestingTabId
        // RequestingOrigin
        // RequestedAt
        // RequestKind
        // RequestData
        // ResponedAt
        // ResponseKind
        // ResponseData
    }

}