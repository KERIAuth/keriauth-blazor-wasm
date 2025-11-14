using static Extension.Services.IStateService;

namespace Extension.Models {
    using Extension.Models.Storage;
    using System.Text.Json.Serialization;

    public record AppState : IStorageModel {
        [JsonConstructor]
        public AppState(Services.IStateService.States currentState) {
            CurrentState = currentState;
            WriteUtc = DateTime.UtcNow;
        }

        [JsonPropertyName("CurrentState")]
        public States CurrentState { get; init; }

        [JsonPropertyName("WriteUtc")]
        public DateTime WriteUtc { get; init; }
    }
}
