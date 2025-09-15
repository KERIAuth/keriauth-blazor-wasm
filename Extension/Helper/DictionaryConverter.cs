using System.Text.Json;
using System.Text.Json.Serialization;

namespace Extension.Helper {
    public class TypedValue(object value, Type type) {
        public object Value { get; set; } = value;
        public Type Type { get; set; } = type;
    }

    /// <summary>
    /// JSON converter for Dictionary&lt;string, object&gt; used at the interop boundary with signify-ts.
    /// For internal credential processing, use RecursiveDictionary which preserves field order.
    /// 
    /// Responsibilities:
    /// - Deserialize JSON from signify-ts into Dictionary&lt;string, object&gt;
    /// - Provide backward compatibility for tests
    /// - Handle the interop boundary where field order may not be preserved
    /// 
    /// For new code working with credentials, use RecursiveDictionary directly.
    /// </summary>
    public class DictionaryConverter : JsonConverter<Dictionary<string, object>> {
        public override Dictionary<string, object> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            if (reader.TokenType != JsonTokenType.StartObject) {
                throw new JsonException();
            }

            var dictionary = new Dictionary<string, object>();

            while (reader.Read()) {
                if (reader.TokenType == JsonTokenType.EndObject) {
                    return dictionary;
                }

                if (reader.TokenType != JsonTokenType.PropertyName) {
                    throw new JsonException();
                }

                string? propertyName = reader.GetString() ?? throw new JsonException("Property name is null");
                reader.Read();

                object value = ReadValue(ref reader, options);
                dictionary.Add(propertyName, value);
            }

            return dictionary;
        }

        private static object ReadValue(ref Utf8JsonReader reader, JsonSerializerOptions options) {
            switch (reader.TokenType) {
                case JsonTokenType.String:
                    return reader.GetString() ?? String.Empty;
                case JsonTokenType.Number:
                    if (reader.TryGetInt64(out long l)) {
                        return l;
                    }
                    return reader.GetDouble();
                case JsonTokenType.True:
                    return true;
                case JsonTokenType.False:
                    return false;
                case JsonTokenType.StartObject:
                    return JsonSerializer.Deserialize<Dictionary<string, object>>(ref reader, options) ?? [];
                case JsonTokenType.StartArray:
                    var list = new List<object>();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) {
                        list.Add(ReadValue(ref reader, options));
                    }
                    return list;
                case JsonTokenType.Null:
                    return new object();
                default:
                    throw new JsonException($"Unexpected token parsing JSON: {reader.TokenType}");
            }
        }


        public override void Write(Utf8JsonWriter writer, Dictionary<string, object> value, JsonSerializerOptions options) {
            throw new NotImplementedException();
        }
    }
}
