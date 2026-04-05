namespace Extension.Tests.Models;

using Extension.Models.Storage;
using Extension.Services.SignifyService.Models;
using System.Text.Json;
using Xunit;

/// <summary>
/// Tests for the CachedIdentifiers session record.
/// </summary>
public class CachedIdentifiersTests {
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void CachedIdentifiers_SerializationRoundTrip() {
        var cached = new CachedIdentifiers {
            IdentifiersList = [
                new Identifiers(0, 1, 1, [new Aid("test", "EPrefix", null!)])
            ]
        };

        var json = JsonSerializer.Serialize(cached, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<CachedIdentifiers>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized.IdentifiersList);
        Assert.Equal("test", deserialized.IdentifiersList[0].Aids[0].Name);
    }

    [Fact]
    public void CachedIdentifiers_IsNotVersioned() {
        var cached = new CachedIdentifiers { IdentifiersList = [] };
        Assert.IsNotAssignableFrom<IVersionedStorageModel>(cached);
        Assert.IsAssignableFrom<IStorageModel>(cached);
    }
}
