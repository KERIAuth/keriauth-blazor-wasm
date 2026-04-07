namespace Extension.Tests.Models;

using Extension.Models.Storage;
using System.Text.Json;
using Xunit;

/// <summary>
/// Tests for the dictionary-based CachedExns session record.
/// </summary>
public class CachedExnsTests {
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void CachedExns_DefaultsToEmptyDictionary() {
        var cached = new CachedExns();
        Assert.NotNull(cached.Exchanges);
        Assert.Empty(cached.Exchanges);
    }

    [Fact]
    public void CachedExns_SerializationRoundTrip() {
        var cached = new CachedExns {
            Exchanges = new Dictionary<string, string> {
                ["EXN_SAID1"] = """{"exn":{"d":"EXN_SAID1","i":"sender","r":"/ipex/grant"}}""",
                ["EXN_SAID2"] = """{"exn":{"d":"EXN_SAID2","i":"other","r":"/ipex/offer"}}"""
            }
        };

        var json = JsonSerializer.Serialize(cached, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<CachedExns>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Exchanges.Count);
        Assert.Contains("EXN_SAID1", deserialized.Exchanges.Keys);
        Assert.Contains("\"r\":\"/ipex/grant\"", deserialized.Exchanges["EXN_SAID1"]);
    }

    [Fact]
    public void CachedExns_IsNotVersioned() {
        var cached = new CachedExns();
        Assert.IsNotAssignableFrom<IVersionedStorageModel>(cached);
        Assert.IsAssignableFrom<IStorageModel>(cached);
    }
}
