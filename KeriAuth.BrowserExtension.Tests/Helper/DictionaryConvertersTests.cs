using KeriAuth.BrowserExtension.Helper.DictionaryConverters;
using Microsoft.VisualStudio.TestPlatform.TestHost;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KeriAuth.BrowserExtension.Tests.Helper
{
    public class DictionaryConvertersTests
    {
        private readonly JsonSerializerOptions _options;

        public DictionaryConvertersTests()
        {
            _options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new DictionaryConverter() }
            };
        }

        [Fact]
        public void GetValueByPathTest()
        {
            string jsonString = @"{
            ""id"": 1,
            ""name"": ""Foo1"",
            ""nested"": {
                ""value"": ""NestedValue1"",
                ""deepNested"": {
                    ""anotherValue"": ""DeepNestedValue1"",
                    ""collection"": [
                        {
                            ""itemId"": 101,
                            ""itemName"": ""Item1""
                        },
                        {
                            ""itemId"": 102,
                            ""itemName"": ""Item2""
                        }
                    ]
                }
            }}";

            Dictionary<string, object> foo = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString, _options);

            // Test id
            var idTypedValue = DictionaryConverter.GetValueByPath(foo, "id");
            Assert.NotNull(idTypedValue);
            Assert.Equal(typeof(long), idTypedValue.Type);
            Assert.Equal(1L, idTypedValue.Value);

            // Test name
            var nameTypedValue = DictionaryConverter.GetValueByPath(foo, "name");
            Assert.NotNull(nameTypedValue);
            Assert.Equal(typeof(string), nameTypedValue.Type);
            Assert.Equal("Foo1", nameTypedValue.Value);

            // Test nested value
            var nestedValueTypedValue = DictionaryConverter.GetValueByPath(foo, "nested.value");
            Assert.NotNull(nestedValueTypedValue);
            Assert.Equal(typeof(string), nestedValueTypedValue.Type);
            Assert.Equal("NestedValue1", nestedValueTypedValue.Value);

            // Test deep nested value
            var deepNestedValueTypedValue = DictionaryConverter.GetValueByPath(foo, "nested.deepNested.anotherValue");
            Assert.NotNull(deepNestedValueTypedValue);
            Assert.Equal(typeof(string), deepNestedValueTypedValue.Type);
            Assert.Equal("DeepNestedValue1", deepNestedValueTypedValue.Value);

            // Test collection
            var collectionTypedValue = DictionaryConverter.GetValueByPath(foo, "nested.deepNested.collection");
            Assert.NotNull(collectionTypedValue);
            Assert.Equal(typeof(List<object>), collectionTypedValue.Type);
            var collection = collectionTypedValue.Value as List<object>;
            Assert.NotNull(collection);
            Assert.Equal(2, collection.Count);

            // Test collection item 1
            Assert.IsType<Dictionary<string, object>>(collection[0]);
            var firstItem = collection[0] as Dictionary<string, object>;
            Assert.NotNull(firstItem);
            Assert.Equal(101L, firstItem["itemId"]);
            Assert.Equal("Item1", firstItem["itemName"]);

            // Test collection item 2
            Assert.IsType<Dictionary<string, object>>(collection[1]);
            var secondItem = collection[1] as Dictionary<string, object>;
            Assert.NotNull(secondItem);
            Assert.Equal(102L, secondItem["itemId"]);
            Assert.Equal("Item2", secondItem["itemName"]);
        }
    }
}
