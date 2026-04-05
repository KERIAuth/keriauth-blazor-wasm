namespace Extension.Tests.Models;

using Extension.Models;
using System.Text.Json;
using Xunit;

/// <summary>
/// Tests for the dictionary-based CachedCredentials session record.
/// </summary>
public class CachedCredentialsTests {
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void CachedCredentials_DefaultsToEmptyDictionary() {
        var cached = new CachedCredentials();
        Assert.NotNull(cached.Credentials);
        Assert.Empty(cached.Credentials);
    }

    [Fact]
    public void CachedCredentials_SerializationRoundTrip() {
        var cached = new CachedCredentials {
            Credentials = new Dictionary<string, string> {
                ["SAID1"] = """{"sad":{"d":"SAID1","i":"issuer"}}""",
                ["SAID2"] = """{"sad":{"d":"SAID2","i":"other"}}"""
            }
        };

        var json = JsonSerializer.Serialize(cached, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<CachedCredentials>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Credentials.Count);
        Assert.Contains("SAID1", deserialized.Credentials.Keys);
        Assert.Contains("\"i\":\"issuer\"", deserialized.Credentials["SAID1"]);
    }

    [Fact]
    public void CachedCredentials_IsNotVersioned() {
        var cached = new CachedCredentials();
        Assert.IsNotAssignableFrom<Extension.Models.Storage.IVersionedStorageModel>(cached);
        Assert.IsAssignableFrom<Extension.Models.Storage.IStorageModel>(cached);
    }
}
