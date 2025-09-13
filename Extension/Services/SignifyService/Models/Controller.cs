using System.Text.Json.Serialization;

namespace Extension.Services.SignifyService.Models
{
    public class Controller
    {
        [JsonPropertyName("state")]
        public ControllerState? State { get; set; }
        
        [JsonPropertyName("ee")]
        public ControllerEe? Ee { get; set; }
    }
}
