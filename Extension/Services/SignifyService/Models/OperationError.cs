using System.Text.Json.Serialization;

namespace Extension.Services.SignifyService.Models {
    /// <summary>
    /// Represents an error that occurred during a signify operation
    /// </summary>
    public record OperationError(
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("code")] string? Code = null,
        [property: JsonPropertyName("details")] Dictionary<string, string>? Details = null
    ) {
        public override string ToString() => Code != null ? $"[{Code}] {Message}" : Message;
    }
}