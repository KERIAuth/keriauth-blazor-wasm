using System.Text.Json.Serialization;

namespace Extension.Services.SignifyService.Models {
    /// <summary>
    /// Represents the KERIA agent configuration.
    /// Matches signify-ts AgentConfig type.
    /// </summary>
    public record AgentConfig(
        [property: JsonPropertyName("iurls")] List<string>? Iurls = null
    );
}
