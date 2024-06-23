using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace KeriAuth.BrowserExtension.Models
{
    public record Websites(
        [property: JsonPropertyName("websites")] List<Website> WebsiteList);
}