using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public record WebsiteConfigList(
        [property: JsonPropertyName("Websites")] List<WebsiteConfig> WebsiteList);
}