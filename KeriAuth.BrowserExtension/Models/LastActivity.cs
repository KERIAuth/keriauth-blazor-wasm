using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public record LastActivity
    {
        [JsonPropertyName("WriteUtc")]
        public DateTime WriteUtc { get; init; } = DateTime.UtcNow;
    }
}
