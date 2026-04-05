namespace Extension.Tests.Models;

using Extension.Models;
using System.Text.Json;
using Xunit;

/// <summary>
/// Tests for flattened Preferences with SelectedKeriaConnectionDigest at top level.
/// </summary>
public class PreferencesTests {
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Preferences_SelectedKeriaConnectionDigest_DefaultsToNull() {
        var prefs = new Preferences();
        Assert.Null(prefs.SelectedKeriaConnectionDigest);
    }

    [Fact]
    public void Preferences_SelectedKeriaConnectionDigest_SerializationRoundTrip() {
        var prefs = new Preferences { SelectedKeriaConnectionDigest = "test-digest-123" };

        var json = JsonSerializer.Serialize(prefs, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<Preferences>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("test-digest-123", deserialized.SelectedKeriaConnectionDigest);
    }

    [Fact]
    public void Preferences_WithExpression_PreservesSelectedDigest() {
        var prefs = new Preferences {
            SelectedKeriaConnectionDigest = "original-digest",
            IsDarkTheme = false
        };

        var updated = prefs with { IsDarkTheme = true };

        Assert.Equal("original-digest", updated.SelectedKeriaConnectionDigest);
        Assert.True(updated.IsDarkTheme);
    }
}
