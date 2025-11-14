using System.Text.Json.Serialization;
using Extension.Models.Storage;

namespace Extension.Models {
    public record WebsiteConfigList(
        [property: JsonPropertyName("Websites")] List<WebsiteConfig> WebsiteList) : IStorageModel;
}
