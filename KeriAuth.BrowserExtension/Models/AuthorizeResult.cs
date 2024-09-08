﻿using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public record AuthorizeResult
    {
        [JsonConstructor]
        public AuthorizeResult(
            AuthorizeResultCredential? arc,
            AuthorizeResultIdentifier? ari
            )
        {
            if (arc is null && ari is null)
            {
                throw new ArgumentException("Either arc or ari must be non-null");
            }

            this.ARCredential = arc;
            this.ARIdentifier = ari;
        }

        [JsonPropertyName("credential")]
        public AuthorizeResultCredential? ARCredential { get; }

        [JsonPropertyName("identifier")]
        public AuthorizeResultIdentifier? ARIdentifier { get; }
    }
}
