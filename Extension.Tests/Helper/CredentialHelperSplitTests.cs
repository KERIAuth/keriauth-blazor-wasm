namespace Extension.Tests.Helper;

using Extension.Helper;
using Xunit;

/// <summary>
/// Tests for CredentialHelper.SplitCredentialsArrayToDict and DeserializeCredentialsDict.
/// </summary>
public class CredentialHelperSplitTests {

    private const string SampleCredentialsJson = """
    [
        {"sad":{"d":"SAID1","i":"issuer1","a":{"i":"holder1"}},"anc":{},"iss":{}},
        {"sad":{"d":"SAID2","i":"issuer2","a":{"i":"holder2"}},"anc":{},"iss":{}}
    ]
    """;

    [Fact]
    public void SplitCredentialsArrayToDict_ExtractsSaidKeys() {
        var result = CredentialHelper.SplitCredentialsArrayToDict(SampleCredentialsJson);

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("SAID1"));
        Assert.True(result.ContainsKey("SAID2"));
    }

    [Fact]
    public void SplitCredentialsArrayToDict_PreservesRawJson() {
        var result = CredentialHelper.SplitCredentialsArrayToDict(SampleCredentialsJson);

        // Each value should be valid JSON containing the original credential
        Assert.Contains("\"d\":\"SAID1\"", result["SAID1"]);
        Assert.Contains("\"i\":\"issuer1\"", result["SAID1"]);
    }

    [Fact]
    public void SplitCredentialsArrayToDict_EmptyArray_ReturnsEmptyDict() {
        var result = CredentialHelper.SplitCredentialsArrayToDict("[]");
        Assert.Empty(result);
    }

    [Fact]
    public void SplitCredentialsArrayToDict_SkipsEntriesWithoutSadD() {
        var json = """[{"other":"data"},{"sad":{"d":"VALID"},"anc":{}}]""";
        var result = CredentialHelper.SplitCredentialsArrayToDict(json);

        Assert.Single(result);
        Assert.True(result.ContainsKey("VALID"));
    }

    [Fact]
    public void DeserializeCredentialsDict_ConvertsToRecursiveDictionary() {
        var dict = CredentialHelper.SplitCredentialsArrayToDict(SampleCredentialsJson);
        var result = CredentialHelper.DeserializeCredentialsDict(dict);

        Assert.Equal(2, result.Count);
        Assert.Equal("SAID1", result[0].GetValueByPath("sad.d")?.Value?.ToString());
        Assert.Equal("SAID2", result[1].GetValueByPath("sad.d")?.Value?.ToString());
    }

    [Fact]
    public void DeserializeCredentialsDict_EmptyDict_ReturnsEmptyList() {
        var result = CredentialHelper.DeserializeCredentialsDict(new Dictionary<string, string>());
        Assert.Empty(result);
    }

    [Fact]
    public void DeserializeCredentialsDict_NullDict_ReturnsEmptyList() {
        var result = CredentialHelper.DeserializeCredentialsDict(null!);
        Assert.Empty(result);
    }

    [Fact]
    public void RoundTrip_SplitThenDeserialize_PreservesCredentials() {
        var dict = CredentialHelper.SplitCredentialsArrayToDict(SampleCredentialsJson);
        var credentials = CredentialHelper.DeserializeCredentialsDict(dict);

        // Verify both credentials are present with correct issuer values
        var issuers = credentials.Select(c => c.GetValueByPath("sad.i")?.Value?.ToString()).OrderBy(x => x).ToList();
        Assert.Equal(["issuer1", "issuer2"], issuers);
    }
}
