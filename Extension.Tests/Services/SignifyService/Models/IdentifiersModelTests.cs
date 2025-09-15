using Extension.Services.SignifyService.Models;
using System.Text.Json;
using Xunit;

namespace Extension.Tests.Services.SignifyService.Models;

/// <summary>
/// Tests for Identifiers model serialization/deserialization.
/// Ensures compatibility with signify-ts identifier list responses.
/// </summary>
public class IdentifiersModelTests
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Identifiers_Deserialization_WithValidJson_ShouldSucceed()
    {
        // Arrange - JSON similar to what signify-ts identifiers().list() returns
        const string json = """
            {
                "start": 0,
                "end": 2,
                "total": 2,
                "aids": [
                    {
                        "name": "alice",
                        "prefix": "EKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u",
                        "salty": {
                            "sxlt": "1AAHnNrLZDNZDs-qlI0pXxdAB7H2_CRbsixhb-YYKqqrUO_",
                            "pidx": 0,
                            "tier": "low",
                            "dcode": "E",
                            "icodes": ["A"],
                            "ncodes": ["A"],
                            "transferable": true
                        }
                    },
                    {
                        "name": "bob",
                        "prefix": "EALkveIFUPvt38xhtgYYJRCCpAGO7WjjHVR37Pawv67E",
                        "salty": {
                            "sxlt": "1AAHnNrLZDNZDs-qlI0pXxdAB7H2_CRbsixhb-YYKqqrUO_",
                            "pidx": 1,
                            "tier": "low",
                            "dcode": "E",
                            "icodes": ["A"],
                            "ncodes": ["A"],
                            "transferable": true
                        }
                    }
                ]
            }
            """;

        // Act
        var identifiers = JsonSerializer.Deserialize<Identifiers>(json, _jsonOptions);

        // Assert
        Assert.NotNull(identifiers);
        Assert.Equal(0, identifiers.Start);
        Assert.Equal(2, identifiers.End);
        Assert.Equal(2, identifiers.Total);
        Assert.NotNull(identifiers.Aids);
        Assert.Equal(2, identifiers.Aids.Count);
        
        var alice = identifiers.Aids[0];
        Assert.Equal("alice", alice.Name);
        Assert.Equal("EKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u", alice.Prefix);
        
        var bob = identifiers.Aids[1];
        Assert.Equal("bob", bob.Name);
        Assert.Equal("EALkveIFUPvt38xhtgYYJRCCpAGO7WjjHVR37Pawv67E", bob.Prefix);
    }

    [Fact]
    public void Identifiers_WithEmptyAids_ShouldDeserializeCorrectly()
    {
        // Arrange
        const string json = """
            {
                "start": 0,
                "end": 0,
                "total": 0,
                "aids": []
            }
            """;

        // Act
        var identifiers = JsonSerializer.Deserialize<Identifiers>(json, _jsonOptions);

        // Assert
        Assert.NotNull(identifiers);
        Assert.Equal(0, identifiers.Start);
        Assert.Equal(0, identifiers.End);
        Assert.Equal(0, identifiers.Total);
        Assert.NotNull(identifiers.Aids);
        Assert.Empty(identifiers.Aids);
    }

    [Fact]
    public void Identifiers_Serialization_ShouldProduceValidJson()
    {
        // Arrange
        var salty = new Salty("sxlt", 0, 0, "stem", "low", "E", ["A"], ["A"], true);
        var aids = new List<Aid>
        {
            new("test-aid", "EKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u", salty)
        };
        var identifiers = new Identifiers(0, 1, 1, aids);

        // Act
        var json = JsonSerializer.Serialize(identifiers, _jsonOptions);

        // Assert
        Assert.NotNull(json);
        Assert.Contains("start", json);
        Assert.Contains("end", json);
        Assert.Contains("total", json);
        Assert.Contains("aids", json);
        Assert.Contains("test-aid", json);
    }

    [Fact]
    public void Identifiers_Constructor_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        const int start = 5;
        const int end = 10;
        const int total = 15;
        var salty2 = new Salty("sxlt", 0, 0, "stem", "low", "E", ["A"], ["A"], true);
        var aids = new List<Aid> { new("test", "prefix", salty2) };

        // Act
        var identifiers = new Identifiers(start, end, total, aids);

        // Assert
        Assert.Equal(start, identifiers.Start);
        Assert.Equal(end, identifiers.End);
        Assert.Equal(total, identifiers.Total);
        Assert.Equal(aids, identifiers.Aids);
    }

    [Fact]
    public void Identifiers_RoundTripSerialization_ShouldPreserveData()
    {
        // Arrange
        var salty = new Salty("sxlt", 0, 0, "stem", "low", "E", ["A"], ["A"], true);
        var originalAids = new List<Aid>
        {
            new("alice", "EKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u", salty),
            new("bob", "EALkveIFUPvt38xhtgYYJRCCpAGO7WjjHVR37Pawv67E", salty)
        };
        var originalIdentifiers = new Identifiers(0, 2, 2, originalAids);

        // Act
        var json = JsonSerializer.Serialize(originalIdentifiers, _jsonOptions);
        var deserializedIdentifiers = JsonSerializer.Deserialize<Identifiers>(json, _jsonOptions);

        // Assert
        Assert.NotNull(deserializedIdentifiers);
        Assert.Equal(originalIdentifiers.Start, deserializedIdentifiers.Start);
        Assert.Equal(originalIdentifiers.End, deserializedIdentifiers.End);
        Assert.Equal(originalIdentifiers.Total, deserializedIdentifiers.Total);
        Assert.Equal(originalIdentifiers.Aids.Count, deserializedIdentifiers.Aids.Count);
        
        for (int i = 0; i < originalIdentifiers.Aids.Count; i++)
        {
            Assert.Equal(originalIdentifiers.Aids[i].Name, deserializedIdentifiers.Aids[i].Name);
            Assert.Equal(originalIdentifiers.Aids[i].Prefix, deserializedIdentifiers.Aids[i].Prefix);
        }
    }

    [Fact]
    public void Identifiers_WithPagination_ShouldHandleCorrectly()
    {
        // Arrange - Testing pagination scenario
        const string json = """
            {
                "start": 10,
                "end": 20,
                "total": 100,
                "aids": [
                    {
                        "name": "aid-10",
                        "prefix": "EKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u"
                    }
                ]
            }
            """;

        // Act
        var identifiers = JsonSerializer.Deserialize<Identifiers>(json, _jsonOptions);

        // Assert
        Assert.NotNull(identifiers);
        Assert.Equal(10, identifiers.Start);
        Assert.Equal(20, identifiers.End);
        Assert.Equal(100, identifiers.Total);
        Assert.Single(identifiers.Aids);
        Assert.Equal("aid-10", identifiers.Aids[0].Name);
    }

    [Fact]
    public void Identifiers_JsonConstructor_ShouldWorkWithJsonDeserialization()
    {
        // Arrange
        const string json = """
            {
                "start": 0,
                "end": 1,
                "total": 1,
                "aids": [
                    {
                        "name": "constructor-test",
                        "prefix": "EKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u"
                    }
                ]
            }
            """;

        // Act
        var identifiers = JsonSerializer.Deserialize<Identifiers>(json, _jsonOptions);

        // Assert
        Assert.NotNull(identifiers);
        Assert.Equal(0, identifiers.Start);
        Assert.Equal(1, identifiers.End);
        Assert.Equal(1, identifiers.Total);
        Assert.Single(identifiers.Aids);
        Assert.Equal("constructor-test", identifiers.Aids[0].Name);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 5, 10)]
    [InlineData(100, 200, 500)]
    public void Identifiers_WithDifferentPaginationValues_ShouldDeserializeCorrectly(int start, int end, int total)
    {
        // Arrange
        var json = $$"""
            {
                "start": {{start}},
                "end": {{end}},
                "total": {{total}},
                "aids": []
            }
            """;

        // Act
        var identifiers = JsonSerializer.Deserialize<Identifiers>(json, _jsonOptions);

        // Assert
        Assert.NotNull(identifiers);
        Assert.Equal(start, identifiers.Start);
        Assert.Equal(end, identifiers.End);
        Assert.Equal(total, identifiers.Total);
        Assert.Empty(identifiers.Aids);
    }
}