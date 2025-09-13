using System.Text.Json;
using Extension.Helper;

namespace Extension.Tests.Helper {
    public class StorageHelperTests {
        [Fact]
        public void ToJsonString_ShouldConvertJsonDocumentToString_NotIndented() {
            // Arrange
            string json = "{\"name\":\"test\",\"value\":123}";
            JsonDocument doc = JsonDocument.Parse(json);

            // Act
            string result = doc.ToJsonString(false);

            // Assert
            Assert.Equal("{\"name\":\"test\",\"value\":123}", result);
        }

        [Fact]
        public void ToJsonString_ShouldConvertJsonDocumentToString_Indented() {
            // Arrange
            string json = "{\"name\":\"test\",\"value\":123}";
            JsonDocument doc = JsonDocument.Parse(json);

            // Act
            string result = doc.ToJsonString(true);

            // Assert
            // Check that the result contains the same data but now has newlines due to indentation
            Assert.Contains("\"name\": \"test\"", result);
            Assert.Contains("\"value\": 123", result);
            Assert.Contains("\n", result);
        }

        [Fact]
        public void ToJsonString_ShouldHandleEmptyJsonDocument() {
            // Arrange
            string json = "{}";
            JsonDocument doc = JsonDocument.Parse(json);

            // Act
            string result = doc.ToJsonString();

            // Assert
            Assert.Equal("{}", result);
        }

        [Fact]
        public void ToJsonString_ShouldHandleComplexNestedJsonDocument() {
            // Arrange
            string json = "{\"person\":{\"name\":\"John\",\"age\":30},\"items\":[1,2,3]}";
            JsonDocument doc = JsonDocument.Parse(json);

            // Act
            string result = doc.ToJsonString();

            // Assert
            Assert.Equal("{\"person\":{\"name\":\"John\",\"age\":30},\"items\":[1,2,3]}", result);
        }
    }
}
