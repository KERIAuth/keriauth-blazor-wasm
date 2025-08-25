using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Models
{
    public record CancelResult
    {
        [JsonConstructor]
        public CancelResult(
            String? cr
            )
        {
            CancelReason = cr;
        }

        [JsonPropertyName("cr")]
        public String? CancelReason { get; }
    }
}
