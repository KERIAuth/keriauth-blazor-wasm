using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Extension.Services.SignifyService.Models {
    /// <summary>
    /// Represents a threshold value that can be either a string or integer.
    /// Used in KERI/ACDC operations for fields like kt, nt, isith, nsith, toad, thold.
    /// </summary>
    public record ThresholdValue {
        public string? StringValue { get; init; }
        public int? IntegerValue { get; init; }
        
        public bool HasValue => StringValue != null || IntegerValue.HasValue;
        
        public static implicit operator ThresholdValue(string value) => new() { StringValue = value };
        public static implicit operator ThresholdValue(int value) => new() { IntegerValue = value };
        
        public override string ToString() => StringValue ?? IntegerValue?.ToString(CultureInfo.InvariantCulture) ?? "";
        
        public object ToObject() => StringValue != null ? StringValue : IntegerValue!.Value;
    }
    
    /// <summary>
    /// JSON converter for ThresholdValue that handles both string and integer values
    /// </summary>
    public class ThresholdValueConverter : JsonConverter<ThresholdValue> {
        public override ThresholdValue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            return reader.TokenType switch {
                JsonTokenType.String => new ThresholdValue { StringValue = reader.GetString() },
                JsonTokenType.Number when reader.TryGetInt32(out int i) => new ThresholdValue { IntegerValue = i },
                JsonTokenType.Null => null,
                _ => throw new JsonException($"Unexpected token type for ThresholdValue: {reader.TokenType}")
            };
        }
        
        public override void Write(Utf8JsonWriter writer, ThresholdValue value, JsonSerializerOptions options) {
            if (value.StringValue != null) {
                writer.WriteStringValue(value.StringValue);
            } else if (value.IntegerValue.HasValue) {
                writer.WriteNumberValue(value.IntegerValue.Value);
            } else {
                writer.WriteNullValue();
            }
        }
    }
    
    /// <summary>
    /// JSON converter for nullable ThresholdValue
    /// </summary>
    public class NullableThresholdValueConverter : JsonConverter<ThresholdValue?> {
        public override ThresholdValue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            if (reader.TokenType == JsonTokenType.Null) {
                return null;
            }
            
            return reader.TokenType switch {
                JsonTokenType.String => new ThresholdValue { StringValue = reader.GetString() },
                JsonTokenType.Number when reader.TryGetInt32(out int i) => new ThresholdValue { IntegerValue = i },
                _ => throw new JsonException($"Unexpected token type for ThresholdValue: {reader.TokenType}")
            };
        }
        
        public override void Write(Utf8JsonWriter writer, ThresholdValue? value, JsonSerializerOptions options) {
            if (value == null) {
                writer.WriteNullValue();
            } else if (value.StringValue != null) {
                writer.WriteStringValue(value.StringValue);
            } else if (value.IntegerValue.HasValue) {
                writer.WriteNumberValue(value.IntegerValue.Value);
            } else {
                writer.WriteNullValue();
            }
        }
    }
}