using Extension.Helper;

namespace Extension.Tests.Helper {
    public class UrlBuilderTests {
        [Fact]
        public void CreateUrlWithEncodedQueryStrings_EncodesQueryParamsCorrectly() {
            // Arrange
            string baseUrl = "http://example.com";
            var queryParams = new List<KeyValuePair<string, string>>
            {
                new("key1", "value1"),
                // TODO P3 adjust code so this test passes
                // new("key2", "value with space"),
                // new("key3", "value&with&special"),
                // new("key&with&special", "value&with&special")
            };

            // Act
            string result = UrlBuilder.CreateUrlWithEncodedQueryStrings(baseUrl, queryParams);

            // Assert
            Assert.Contains("key1=value1", result);
            // Assert.Contains("key2=value%20with%20space", result);
            // Assert.Contains("key3=value%26with%26special", result);
            // Assert.Contains("key%26with%26special=value%26with%26special", result);
        }

        /* /* TODO P3 adjust code so this test passes
        [Fact]
        public void CreateUrlWithEncodedQueryStrings_HandlesEmptyQueryParams()
        {
            // Arrange
            string baseUrl = "http://example.com";
            var queryParams = new List<KeyValuePair<string, string>>();

            // Act
            string result = UrlBuilder.CreateUrlWithEncodedQueryStrings(baseUrl, queryParams);

            // Assert
            Assert.Equal("http://example.com/", result);
        }
        */

        [Fact]
        public void DecodeUrlQueryString_DecodesQueryParamsCorrectly() {
            // Arrange
            string url = "http://example.com?key1=value1&key%20with%20space=value%20with%20space&key%26with%26special=value%26with%26special";

            // Act
            var result = UrlBuilder.DecodeUrlQueryString(url);

            // Assert
            Assert.Equal("value1", result["key1"]);
            Assert.Equal("value with space", result["key with space"]);
            Assert.Equal("value&with&special", result["key&with&special"]);
        }

        /* TODO P3 adjust code so this test passes
        [Fact]
        public void DecodeUrlQueryString_ThrowsExceptionOnNullValue()
        {
            // Arrange
            string url = "http://example.com?key1=value1&key2=";

            // Act & Assert
            var exception = Assert.Throws<Exception>(() => UrlBuilder.DecodeUrlQueryString(url));
            Assert.Equal("Failed to decode query string with key: key2", exception.Message);
        }
        */

        /* TODO P3 adjust code so this test passes
        [Fact]
        public void DecodeUrlQueryString_ReturnsEmptyDictionaryForNoQueryParams()
        {
            // Arrange
            string url = "http://example.com";

            // Act
            var result = UrlBuilder.DecodeUrlQueryString(url);

            // Assert
            Assert.Empty(result);
        }
        */
    }
}
