using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public record WebsiteConfigList(
        [property: JsonPropertyName("websites")] List<WebsiteConfig> WebsiteList);
}