using static KeriAuth.BrowserExtension.Services.IStateService;

namespace KeriAuth.BrowserExtension.Models
{
    using System.Text.Json.Serialization;

    [method: JsonConstructor]
    public class AppState(Services.IStateService.States currentState)
    {
        [JsonPropertyName("CurrentState")]
        public States CurrentState { get; } = currentState;

        [JsonPropertyName("WriteUtc")]
        public DateTime WriteUtc { get; } = DateTime.UtcNow;
    }
}