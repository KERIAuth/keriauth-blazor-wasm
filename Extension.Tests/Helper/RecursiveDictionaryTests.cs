using Extension.Helper;
using System.Text.Json;
using Xunit;

namespace Extension.Tests.Helper {
    public class RecursiveDictionaryTests {
        private static readonly JsonSerializerOptions JsonOptions = new() {
            Converters = { new RecursiveDictionaryConverter() }
        };
        [Fact]
        public void Should_Store_And_Retrieve_String_Values() {
            var dict = new RecursiveDictionary {
                ["name"] = "John Doe",
                ["email"] = "john@example.com"
            };
            
            Assert.Equal("John Doe", dict["name"].StringValue);
            Assert.Equal("john@example.com", dict["email"].StringValue);
            Assert.Equal(RecursiveValueType.StringType, dict["name"].Type);
        }
        
        [Fact]
        public void Should_Store_And_Retrieve_Numeric_Values() {
            var dict = new RecursiveDictionary {
                ["age"] = 30,
                ["height"] = 5.9
            };
            
            Assert.Equal(30, dict["age"].IntegerValue);
            Assert.Equal(5.9, dict["height"].DoubleValue);
            Assert.Equal(RecursiveValueType.IntegerType, dict["age"].Type);
            Assert.Equal(RecursiveValueType.DoubleType, dict["height"].Type);
        }
        
        [Fact]
        public void Should_Store_And_Retrieve_Boolean_Values() {
            var dict = new RecursiveDictionary {
                ["isActive"] = true,
                ["isDeleted"] = false
            };
            
            Assert.True(dict["isActive"].BooleanValue);
            Assert.False(dict["isDeleted"].BooleanValue);
            Assert.Equal(RecursiveValueType.BooleanType, dict["isActive"].Type);
        }
        
        [Fact]
        public void Should_Store_And_Retrieve_Nested_Dictionaries() {
            var innerDict = new RecursiveDictionary {
                ["street"] = "123 Main St",
                ["city"] = "New York"
            };
            
            var dict = new RecursiveDictionary {
                ["name"] = "John Doe",
                ["address"] = innerDict
            };
            
            Assert.NotNull(dict["address"].Dictionary);
            Assert.Equal("123 Main St", dict["address"].Dictionary!["street"].StringValue);
            Assert.Equal("New York", dict["address"].Dictionary!["city"].StringValue);
            Assert.Equal(RecursiveValueType.Dictionary, dict["address"].Type);
        }
        
        [Fact]
        public void Should_Get_Value_By_Path() {
            var dict = new RecursiveDictionary {
                ["user"] = new RecursiveDictionary {
                    ["profile"] = new RecursiveDictionary {
                        ["name"] = "John Doe",
                        ["age"] = 30
                    }
                }
            };
            
            var nameValue = dict.GetByPath("user.profile.name");
            Assert.NotNull(nameValue);
            Assert.Equal("John Doe", nameValue!.StringValue);
            
            var ageValue = dict.GetByPath("user.profile.age");
            Assert.NotNull(ageValue);
            Assert.Equal(30, ageValue!.IntegerValue);
            
            var notFound = dict.GetByPath("user.profile.nonexistent");
            Assert.Null(notFound);
        }
        
        [Fact]
        public void Should_Set_Value_By_Path_Creating_Intermediate_Dictionaries() {
            var dict = new RecursiveDictionary();
            
            dict.SetByPath("user.profile.name", "John Doe");
            dict.SetByPath("user.profile.age", 30);
            dict.SetByPath("user.settings.theme", "dark");
            
            Assert.NotNull(dict["user"].Dictionary);
            Assert.NotNull(dict["user"].Dictionary!["profile"].Dictionary);
            Assert.Equal("John Doe", dict["user"].Dictionary!["profile"].Dictionary!["name"].StringValue);
            Assert.Equal(30, dict["user"].Dictionary!["profile"].Dictionary!["age"].IntegerValue);
            Assert.Equal("dark", dict["user"].Dictionary!["settings"].Dictionary!["theme"].StringValue);
        }
        
        [Fact]
        public void Should_Convert_From_Object_Dictionary() {
            var objectDict = new Dictionary<string, object> {
                ["name"] = "John",
                ["age"] = 30,
                ["active"] = true,
                ["nested"] = new Dictionary<string, object> {
                    ["key"] = "value"
                }
            };
            
            var recursiveDict = RecursiveDictionary.FromObjectDictionary(objectDict);
            
            Assert.Equal("John", recursiveDict["name"].StringValue);
            Assert.Equal(30, recursiveDict["age"].IntegerValue);
            Assert.True(recursiveDict["active"].BooleanValue);
            Assert.NotNull(recursiveDict["nested"].Dictionary);
            Assert.Equal("value", recursiveDict["nested"].Dictionary!["key"].StringValue);
        }
        
        [Fact]
        public void Should_Convert_To_Object_Dictionary() {
            var recursiveDict = new RecursiveDictionary {
                ["name"] = "John",
                ["age"] = 30,
                ["nested"] = new RecursiveDictionary {
                    ["key"] = "value"
                }
            };
            
            var objectDict = recursiveDict.ToObjectDictionary();
            
            Assert.Equal("John", objectDict["name"]);
            Assert.Equal(30L, objectDict["age"]);
            Assert.IsType<Dictionary<string, object>>(objectDict["nested"]);
            var nested = (Dictionary<string, object>)objectDict["nested"];
            Assert.Equal("value", nested["key"]);
        }
        
        [Fact]
        public void Should_Handle_Lists() {
            var dict = new RecursiveDictionary();
            var list = new List<RecursiveValue> {
                "item1",
                42,
                new RecursiveDictionary { ["nested"] = "value" }
            };
            
            dict["items"] = new RecursiveValue { List = list };
            
            Assert.NotNull(dict["items"].List);
            Assert.Equal(3, dict["items"].List!.Count);
            Assert.Equal("item1", dict["items"].List![0].StringValue);
            Assert.Equal(42, dict["items"].List![1].IntegerValue);
            Assert.NotNull(dict["items"].List![2].Dictionary);
            Assert.Equal("value", dict["items"].List![2].Dictionary!["nested"].StringValue);
        }
        
        [Fact]
        public void Should_Serialize_And_Deserialize_With_Json() {
            var dict = new RecursiveDictionary {
                ["name"] = "John",
                ["age"] = 30,
                ["active"] = true,
                ["score"] = 95.5,
                ["address"] = new RecursiveDictionary {
                    ["street"] = "123 Main St",
                    ["city"] = "New York"
                },
                ["tags"] = new RecursiveValue { 
                    List = new List<RecursiveValue> { "tag1", "tag2", "tag3" } 
                }
            };
            
            var options = new JsonSerializerOptions();
            options.Converters.Add(new RecursiveDictionaryConverter());
            
            var json = JsonSerializer.Serialize(dict, options);
            var deserialized = JsonSerializer.Deserialize<RecursiveDictionary>(json, options);
            
            Assert.NotNull(deserialized);
            Assert.Equal("John", deserialized!["name"].StringValue);
            Assert.Equal(30, deserialized["age"].IntegerValue);
            Assert.True(deserialized["active"].BooleanValue);
            Assert.Equal(95.5, deserialized["score"].DoubleValue);
            Assert.NotNull(deserialized["address"].Dictionary);
            Assert.Equal("123 Main St", deserialized["address"].Dictionary!["street"].StringValue);
            Assert.NotNull(deserialized["tags"].List);
            Assert.Equal(3, deserialized["tags"].List!.Count);
            Assert.Equal("tag1", deserialized["tags"].List![0].StringValue);
        }
        
        [Fact]
        public void Should_Handle_Null_Values() {
            var dict = new RecursiveDictionary();
            dict["nullValue"] = new RecursiveValue { IsNull = true };
            
            Assert.True(dict["nullValue"].IsNull);
            Assert.Equal(RecursiveValueType.Null, dict["nullValue"].Type);
            
            var objectDict = dict.ToObjectDictionary();
            Assert.Null(objectDict["nullValue"]);
        }
        
        [Fact]
        public void Should_Preserve_Field_Order() {
            // This is critical for KERI/ACDC operations
            var dict = new RecursiveDictionary();
            dict["z"] = "last";
            dict["a"] = "first";
            dict["m"] = "middle";
            
            var keys = dict.Keys.ToList();
            Assert.Equal("z", keys[0]);
            Assert.Equal("a", keys[1]);
            Assert.Equal("m", keys[2]);
            
            // Verify order is preserved during serialization
            var json = JsonSerializer.Serialize(dict, JsonOptions);
            // The JSON should maintain the insertion order
            var zIndex = json.IndexOf("\"z\"", StringComparison.Ordinal);
            var aIndex = json.IndexOf("\"a\"", StringComparison.Ordinal);
            var mIndex = json.IndexOf("\"m\"", StringComparison.Ordinal);
            
            Assert.True(zIndex < aIndex);
            Assert.True(aIndex < mIndex);
        }
    }
}