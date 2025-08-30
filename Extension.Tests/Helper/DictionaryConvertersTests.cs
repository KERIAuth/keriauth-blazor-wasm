using Extension.Helper;
using System.Text.Json;


namespace Extension.Tests.Helper
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
            ""truthy"": true,
            ""falsy"": false,
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
                            ""itemName"": ""Item2_asdf asdf""
                        }
                    ]
                }
            }}";

            Dictionary<string, object> foo = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString, _options) ?? [];

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

            // Test truthy
            var truthyTypedValue = DictionaryConverter.GetValueByPath(foo, "truthy");
            Assert.NotNull(truthyTypedValue);
            Assert.Equal(typeof(bool), truthyTypedValue.Type);
            Assert.True((bool)truthyTypedValue.Value);

            // Test falsy
            var falsyTypedValue = DictionaryConverter.GetValueByPath(foo, "falsy");
            Assert.NotNull(falsyTypedValue);
            Assert.Equal(typeof(bool), falsyTypedValue.Type);
            Assert.False((bool)falsyTypedValue.Value);


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
            Assert.Equal("Item2_asdf asdf", secondItem["itemName"]);
        }
    }
}
