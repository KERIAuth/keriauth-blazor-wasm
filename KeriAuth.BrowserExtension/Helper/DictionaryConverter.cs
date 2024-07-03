using System.Diagnostics;
using System.Text.Json;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Helper.DictionaryConverters
{
    public class TypedValue
    {
        public object Value { get; set; }
        public Type Type { get; set; }

        public TypedValue(object value, Type type)
        {
            Value = value;
            Type = type;
        }
    }

    public class DictionaryConverter : JsonConverter<Dictionary<string, object>>
    {
        public override Dictionary<string, object> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException();
            }

            var dictionary = new Dictionary<string, object>();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return dictionary;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException();
                }

                string propertyName = reader.GetString();
                if (propertyName == null)
                {
                    throw new JsonException("Property name is null");
                }

                reader.Read();

                object value = ReadValue(ref reader, options);
                dictionary.Add(propertyName, value);
            }

            return dictionary;
        }

        private object ReadValue(ref Utf8JsonReader reader, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    return reader.GetString();
                case JsonTokenType.Number:
                    if (reader.TryGetInt64(out long l))
                    {
                        return l;
                    }
                    return reader.GetDouble();
                case JsonTokenType.True:
                    return true;
                case JsonTokenType.False:
                    return false;
                case JsonTokenType.StartObject:
                    return JsonSerializer.Deserialize<Dictionary<string, object>>(ref reader, options);
                case JsonTokenType.StartArray:
                    var list = new List<object>();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        list.Add(ReadValue(ref reader, options));
                    }
                    return list;
                case JsonTokenType.Null:
                    return null;
                default:
                    throw new JsonException($"Unexpected token parsing JSON: {reader.TokenType}");
            }
        }

        public static TypedValue GetValueByPath(Dictionary<string, object> dictionary, string path)
        {
            string[] keys = path.Split('.');
            object current = dictionary;

            foreach (var key in keys)
            {
                if (current is Dictionary<string, object> currentDict && currentDict.TryGetValue(key, out var value))
                {
                    current = value;
                }
                else
                {
                    return null;
                }
            }

            return new TypedValue(current, current?.GetType());
        }

        public override void Write(Utf8JsonWriter writer, Dictionary<string, object> value, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }
    }
}
