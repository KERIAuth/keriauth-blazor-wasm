using System.Text.Json.Serialization;
namespace KeriAuth.BrowserExtension.Models
{
    public record WebsiteConfig
    {
        public WebsiteConfig(
        Uri origin,
        List<WebsiteInteraction> interactions,
        string? rememberedPrefixOrNothing,
        string? rememberedCredSaidOrNothing,
        bool isAutoSignInIdentifier,
        bool isAutoSignInCredential,
        bool isAutoSignHeaders
        )
        {
            Origin = origin;
            Interactions = interactions;
            RememberedPrefixOrNothing = rememberedPrefixOrNothing;
            RememberedCredSaidOrNothing = rememberedCredSaidOrNothing;
            IsAutoSignInIdentifier = isAutoSignInIdentifier;
            IsAutoSignInCredential = isAutoSignInCredential;
            IsAutoSignHeaders = isAutoSignHeaders;
        }

        /* 
        public WebsiteConfig Validate()
        {
            if (IsAutoSignInIdentifier && RememberedPrefixOrNothing is null)
            {
                throw new ArgumentException("WebsiteConfig constructor is inconsistent on identifier.");
            }
            if (IsAutoSignInCredential && (RememberedCredSaidOrNothing is null || !IsAutoSignInIdentifier))
            {
                throw new ArgumentException("WebsiteConfig constructor is inconsistent on credential.");
            }
            if (RememberedCredSaidOrNothing is not null && RememberedPrefixOrNothing is null)
            {
                throw new ArgumentException("WebsiteConfig constructor with credential set must also have identifier set.");
            }
            if (IsAutoSignHeaders && RememberedCredSaidOrNothing is null)
            {
                throw new ArgumentException("WebsiteConfig constructor with IsAuthoSignHeaders must have a remembered credential.");
            }
            return this;
        }
        */

        [JsonPropertyName("origin")]
        public Uri Origin { get; init; }

        [JsonPropertyName("websiteInteractions")]
        public List<WebsiteInteraction> Interactions { get; init; }

        [JsonPropertyName("rememberedPrefixOrNothing")]
        public string? RememberedPrefixOrNothing { get; init; }

        [JsonPropertyName("rememberedCredSaidOrNothing")]
        public string? RememberedCredSaidOrNothing { get; init; }

        //[JsonPropertyName("autoSignInChoice")]
        //public AutoSignInMode AutoSignInChoice { get; init; }

        [JsonPropertyName("isAutoSignInIdentifier")]
        public bool IsAutoSignInIdentifier { get; init; }

        [JsonPropertyName("isAutoSignInCredential")]
        public bool IsAutoSignInCredential { get; init; }

        [JsonPropertyName("isAutoSignHeaders")]
        public bool IsAutoSignHeaders { get; init; }
    }

    //public enum AutoSignInMode
    //{
    //    None,
    //    Identifier,
    //    Credential
    //}

    public record WebsiteInteraction
    {
        // TODO P2 Implement this record WebsiteInteraction
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