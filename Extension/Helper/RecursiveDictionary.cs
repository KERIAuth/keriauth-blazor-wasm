using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Extension.Helper {
    /// <summary>
    /// Represents a recursive dictionary structure that can contain nested dictionaries,
    /// primitive values (string, bool, int, double), lists, or null values.
    /// This is particularly useful for KERI/ACDC operations where field order matters.
    /// </summary>
    public class RecursiveDictionary : Dictionary<string, RecursiveValue> {
        public RecursiveDictionary() : base() { }
        
        public RecursiveDictionary(int capacity) : base(capacity) { }
        
        public RecursiveDictionary(IDictionary<string, RecursiveValue> dictionary) : base(dictionary) { }
        
        /// <summary>
        /// Creates a RecursiveDictionary from a Dictionary&lt;string, object&gt;
        /// </summary>
        public static RecursiveDictionary FromObjectDictionary(Dictionary<string, object> source) {
            var result = new RecursiveDictionary();
            foreach (var kvp in source) {
                result[kvp.Key] = RecursiveValue.FromObject(kvp.Value);
            }
            return result;
        }
        
        /// <summary>
        /// Converts to Dictionary&lt;string, object&gt; for compatibility
        /// </summary>
        public Dictionary<string, object> ToObjectDictionary() {
            var result = new Dictionary<string, object>();
            foreach (var kvp in this) {
                result[kvp.Key] = kvp.Value.ToObject();
            }
            return result;
        }
        
        /// <summary>
        /// Gets a value by dot-separated path (e.g., "a.b.c")
        /// </summary>
        public RecursiveValue? GetByPath(string path) {
            string[] keys = path.Split('.');
            RecursiveValue? current = new RecursiveValue { Dictionary = this };
            
            foreach (var key in keys) {
                if (current?.Dictionary != null && current.Dictionary.TryGetValue(key, out var value)) {
                    current = value;
                } else {
                    return null;
                }
            }
            
            return current;
        }
        
        /// <summary>
        /// Sets a value by dot-separated path, creating intermediate dictionaries as needed
        /// </summary>
        public void SetByPath(string path, RecursiveValue value) {
            string[] keys = path.Split('.');
            RecursiveDictionary current = this;
            
            for (int i = 0; i < keys.Length - 1; i++) {
                if (!current.TryGetValue(keys[i], out var next) || next.Dictionary == null) {
                    var newDict = new RecursiveDictionary();
                    current[keys[i]] = new RecursiveValue { Dictionary = newDict };
                    current = newDict;
                } else {
                    current = next.Dictionary;
                }
            }
            
            current[keys[^1]] = value;
        }
        
        /// <summary>
        /// Gets a value by dot-separated path and returns it as TypedValue for compatibility
        /// </summary>
        public TypedValue? GetValueByPath(string path) {
            var recursiveValue = GetByPath(path);
            if (recursiveValue == null) {
                return null;
            }
            
            var value = recursiveValue.Value;
            if (value == null) {
                return null;
            }
            
            return new TypedValue(value, value.GetType());
        }
    }
    
    /// <summary>
    /// Represents a value in the RecursiveDictionary that can be one of several types
    /// </summary>
    public class RecursiveValue {
        public RecursiveDictionary? Dictionary { get; set; }
        public string? StringValue { get; set; }
        public bool? BooleanValue { get; set; }
        public long? IntegerValue { get; set; }
        public double? DoubleValue { get; set; }
        public List<RecursiveValue>? List { get; set; }
        public bool IsNull { get; set; }
        
        /// <summary>
        /// Gets the actual value as an object for compatibility with TypedValue
        /// </summary>
        public object? Value => ToObject();
        
        /// <summary>
        /// Gets the type of value stored
        /// </summary>
        public RecursiveValueType Type {
            get {
                if (Dictionary != null) return RecursiveValueType.Dictionary;
                if (StringValue != null) return RecursiveValueType.StringType;
                if (BooleanValue.HasValue) return RecursiveValueType.BooleanType;
                if (IntegerValue.HasValue) return RecursiveValueType.IntegerType;
                if (DoubleValue.HasValue) return RecursiveValueType.DoubleType;
                if (List != null) return RecursiveValueType.List;
                if (IsNull) return RecursiveValueType.Null;
                return RecursiveValueType.Undefined;
            }
        }
        
        /// <summary>
        /// Creates a RecursiveValue from an object
        /// </summary>
        public static RecursiveValue FromObject(object? obj) {
            if (obj == null) {
                return new RecursiveValue { IsNull = true };
            }
            
            return obj switch {
                Dictionary<string, object> dict => new RecursiveValue { 
                    Dictionary = RecursiveDictionary.FromObjectDictionary(dict) 
                },
                RecursiveDictionary rDict => new RecursiveValue { Dictionary = rDict },
                string s => new RecursiveValue { StringValue = s },
                bool b => new RecursiveValue { BooleanValue = b },
                int i => new RecursiveValue { IntegerValue = i },
                long l => new RecursiveValue { IntegerValue = l },
                float f => new RecursiveValue { DoubleValue = f },
                double d => new RecursiveValue { DoubleValue = d },
                IEnumerable enumerable => new RecursiveValue { 
                    List = enumerable.Cast<object>().Select(FromObject).ToList() 
                },
                _ => new RecursiveValue { StringValue = obj.ToString() }
            };
        }
        
        /// <summary>
        /// Converts to object for compatibility with existing code
        /// </summary>
        public object ToObject() {
            return Type switch {
                RecursiveValueType.Dictionary => Dictionary!.ToObjectDictionary(),
                RecursiveValueType.StringType => StringValue!,
                RecursiveValueType.BooleanType => BooleanValue!.Value,
                RecursiveValueType.IntegerType => IntegerValue!.Value,
                RecursiveValueType.DoubleType => DoubleValue!.Value,
                RecursiveValueType.List => List!.Select(v => v.ToObject()).ToList(),
                RecursiveValueType.Null => null!,
                _ => new object()
            };
        }
        
        // Implicit conversions for convenience
        public static implicit operator RecursiveValue(string value) => new() { StringValue = value };
        public static implicit operator RecursiveValue(bool value) => new() { BooleanValue = value };
        public static implicit operator RecursiveValue(int value) => new() { IntegerValue = value };
        public static implicit operator RecursiveValue(long value) => new() { IntegerValue = value };
        public static implicit operator RecursiveValue(double value) => new() { DoubleValue = value };
        public static implicit operator RecursiveValue(RecursiveDictionary value) => new() { Dictionary = value };
    }
    
    public enum RecursiveValueType {
        Undefined,
        Dictionary,
        StringType,
        BooleanType,
        IntegerType,
        DoubleType,
        List,
        Null
    }
    
    /// <summary>
    /// JSON converter for RecursiveDictionary that preserves field order
    /// </summary>
    public class RecursiveDictionaryConverter : JsonConverter<RecursiveDictionary> {
        public override RecursiveDictionary Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            if (reader.TokenType != JsonTokenType.StartObject) {
                throw new JsonException("Expected start of object");
            }
            
            var dictionary = new RecursiveDictionary();
            
            while (reader.Read()) {
                if (reader.TokenType == JsonTokenType.EndObject) {
                    return dictionary;
                }
                
                if (reader.TokenType != JsonTokenType.PropertyName) {
                    throw new JsonException("Expected property name");
                }
                
                string propertyName = reader.GetString() ?? throw new JsonException("Property name is null");
                reader.Read();
                
                dictionary[propertyName] = ReadValue(ref reader, options);
            }
            
            return dictionary;
        }
        
        private RecursiveValue ReadValue(ref Utf8JsonReader reader, JsonSerializerOptions options) {
            return reader.TokenType switch {
                JsonTokenType.String => new RecursiveValue { StringValue = reader.GetString() },
                JsonTokenType.Number when reader.TryGetInt64(out long l) => new RecursiveValue { IntegerValue = l },
                JsonTokenType.Number => new RecursiveValue { DoubleValue = reader.GetDouble() },
                JsonTokenType.True => new RecursiveValue { BooleanValue = true },
                JsonTokenType.False => new RecursiveValue { BooleanValue = false },
                JsonTokenType.StartObject => new RecursiveValue { 
                    Dictionary = JsonSerializer.Deserialize<RecursiveDictionary>(ref reader, options) 
                },
                JsonTokenType.StartArray => ReadArray(ref reader, options),
                JsonTokenType.Null => new RecursiveValue { IsNull = true },
                _ => throw new JsonException($"Unexpected token: {reader.TokenType}")
            };
        }
        
        private RecursiveValue ReadArray(ref Utf8JsonReader reader, JsonSerializerOptions options) {
            var list = new List<RecursiveValue>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) {
                list.Add(ReadValue(ref reader, options));
            }
            return new RecursiveValue { List = list };
        }
        
        public override void Write(Utf8JsonWriter writer, RecursiveDictionary value, JsonSerializerOptions options) {
            writer.WriteStartObject();
            
            foreach (var kvp in value) {
                writer.WritePropertyName(kvp.Key);
                WriteValue(writer, kvp.Value, options);
            }
            
            writer.WriteEndObject();
        }
        
        private void WriteValue(Utf8JsonWriter writer, RecursiveValue value, JsonSerializerOptions options) {
            switch (value.Type) {
                case RecursiveValueType.Dictionary:
                    Write(writer, value.Dictionary!, options);
                    break;
                case RecursiveValueType.StringType:
                    writer.WriteStringValue(value.StringValue);
                    break;
                case RecursiveValueType.BooleanType:
                    writer.WriteBooleanValue(value.BooleanValue!.Value);
                    break;
                case RecursiveValueType.IntegerType:
                    writer.WriteNumberValue(value.IntegerValue!.Value);
                    break;
                case RecursiveValueType.DoubleType:
                    writer.WriteNumberValue(value.DoubleValue!.Value);
                    break;
                case RecursiveValueType.List:
                    writer.WriteStartArray();
                    foreach (var item in value.List!) {
                        WriteValue(writer, item, options);
                    }
                    writer.WriteEndArray();
                    break;
                case RecursiveValueType.Null:
                    writer.WriteNullValue();
                    break;
                default:
                    writer.WriteNullValue();
                    break;
            }
        }
    }
}