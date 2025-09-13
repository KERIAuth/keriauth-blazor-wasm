using System.Text.Json;
using Extension.Helper;

namespace Extension.Tests.Helper {



    public class DictionaryConverterTests {
        private static JsonSerializerOptions CreateDefaultSerializerOptions() {
            var options = new JsonSerializerOptions {
                Converters = { new DictionaryConverter() }
            };
            return options;
        }

        [Fact]
        public void Read_ShouldParseNestedJsonObject() {
            // Arrange
            string json = @"{
                ""name"": ""John"",
                ""age"": 30,
                ""isActive"": true,
                ""address"": {
                    ""street"": ""Main St"",
                    ""city"": ""New York""
                },
                ""tags"": [""developer"", ""dotnet""]
            }";

            var options = CreateDefaultSerializerOptions();

            // Act
            var result = JsonSerializer.Deserialize<Dictionary<string, object>>(json, options);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("John", result["name"]);
            Assert.Equal(30L, result["age"]);
            Assert.True((bool)result["isActive"]);
            Assert.IsType<Dictionary<string, object>>(result["address"]);
            var address = (Dictionary<string, object>)result["address"];
            Assert.Equal("Main St", address["street"]);
            Assert.Equal("New York", address["city"]);
            Assert.IsType<List<object>>(result["tags"]);
            var tags = (List<object>)result["tags"];
            Assert.Equal(2, tags.Count);
            Assert.Equal("developer", tags[0]);
            Assert.Equal("dotnet", tags[1]);
        }

        [Fact]
        public void Read_ShouldThrowJsonExceptionForInvalidJson() {
            // Arrange
            const string V = "{ this is not valid json }";
            string invalidJson = V;
            var options = new JsonSerializerOptions {
                Converters = { new DictionaryConverter() }
            };

            // Act & Assert
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Dictionary<string, object>>(invalidJson, options));
        }

        [Fact]
        public void GetValueByPath_ShouldReturnValueForValidPath() {
            // Arrange
            var dictionary = new Dictionary<string, object> {
                ["name"] = "John",
                ["address"] = new Dictionary<string, object> {
                    ["city"] = "New York",
                    ["zip"] = 10001
                }
            };

            // Act
            var result = DictionaryConverter.GetValueByPath(dictionary, "address.city");

            // Assert
            Assert.NotNull(result);
            Assert.Equal("New York", result.Value);
            Assert.Equal(typeof(string), result.Type);
        }

        [Fact]
        public void GetValueByPath_ShouldReturnNullForInvalidPath() {
            // Arrange
            var dictionary = new Dictionary<string, object> {
                ["name"] = "John",
                ["address"] = new Dictionary<string, object> {
                    ["city"] = "New York"
                }
            };

            // Act
            var result = DictionaryConverter.GetValueByPath(dictionary, "address.street");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void Write_ShouldThrowNotImplementedException() {
            // Arrange
            var converter = new DictionaryConverter();
            var dictionary = new Dictionary<string, object>();
            var options = new JsonSerializerOptions();
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream);

            // Act & Assert
            Assert.Throws<NotImplementedException>(() => converter.Write(writer, dictionary, options));
        }
    }
}
