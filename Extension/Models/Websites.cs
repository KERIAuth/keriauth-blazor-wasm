using System.Text.Json.Serialization;

namespace Extension.Models {
    public record WebsiteConfigList(
        [property: JsonPropertyName("Websites")] List<WebsiteConfig> WebsiteList);
}
