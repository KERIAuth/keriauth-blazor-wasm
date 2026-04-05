using System.Text.Json.Serialization;
using Extension.Models.Storage;

namespace Extension.Models {
    public record WebsiteConfigList : IVersionedStorageModel {
        [JsonPropertyName("SchemaVersion")]
        public int SchemaVersion { get; init; } = 1;

        [JsonPropertyName("Websites")]
        public List<WebsiteConfig> WebsiteList { get; init; } = [];
    }
}
