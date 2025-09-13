using System.Text.Json.Serialization;

namespace Extension.Services.SignifyService.Models {
    public record Controller {
        [JsonPropertyName("state")]
        public ControllerState? State { get; init; }

        [JsonPropertyName("ee")]
        public ControllerEe? Ee { get; init; }
    }
}
