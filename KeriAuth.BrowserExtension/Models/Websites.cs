using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace KeriAuth.BrowserExtension.Models
{
    public record WebsiteConfigList(
        [property: JsonPropertyName("websites")] List<WebsiteConfig> WebsiteList);
}