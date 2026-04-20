using System.Text.Json;
using System.Text.Json.Serialization;

namespace Extension.Services.SignifyService.Models {
    /// <summary>
    /// Represents an error that occurred during a signify operation.
    /// KERIA returns <c>error.code</c> as a string most of the time, but as a number when the
    /// underlying failure is an HTTP-status-coded operation (e.g. OOBI fetch 404/500). The
    /// converter coerces either shape into a string so the rest of the extension doesn't care.
    /// </summary>
    public record OperationError(
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("code"), JsonConverter(typeof(StringOrNumberToStringConverter))] string? Code = null,
        [property: JsonPropertyName("details")] Dictionary<string, string>? Details = null
    ) {
        public override string ToString() => Code != null ? $"[{Code}] {Message}" : Message;
    }

    internal sealed class StringOrNumberToStringConverter : JsonConverter<string?> {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch {
                JsonTokenType.Null => null,
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => reader.TryGetInt64(out var i)
                    ? i.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
                JsonTokenType.True => "true",
                JsonTokenType.False => "false",
                _ => throw new JsonException($"Expected string or number for error code, got {reader.TokenType}")
            };

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) {
            if (value is null) writer.WriteNullValue();
            else writer.WriteStringValue(value);
        }
    }
}
