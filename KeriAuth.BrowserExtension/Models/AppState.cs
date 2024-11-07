using static KeriAuth.BrowserExtension.Services.IStateService;

namespace KeriAuth.BrowserExtension.Models
{
    using System.Text.Json.Serialization;

    [method: JsonConstructor]
    public class AppState(Services.IStateService.States currentState)
    {
        public States CurrentState { get; } = currentState;
        public DateTime WriteUtc { get; } = DateTime.UtcNow;
    }
}