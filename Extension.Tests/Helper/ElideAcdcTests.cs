using Extension.Helper;

namespace Extension.Tests.Helper;

public class ElideAcdcTests {
    private static RecursiveDictionary BuildSampleAcdc() {
        var acdc = new RecursiveDictionary();
        acdc["d"] = new RecursiveValue { StringValue = "EOriginalTopLevelSAID" };
        acdc["v"] = new RecursiveValue { StringValue = "ACDC10JSON000000_" };
        acdc["i"] = new RecursiveValue { StringValue = "EIssuerPrefix" };
        acdc["s"] = new RecursiveValue { StringValue = "ESchemaSAID" };

        var attrs = new RecursiveDictionary();
        attrs["d"] = new RecursiveValue { StringValue = "EAttrSAID" };
        attrs["i"] = new RecursiveValue { StringValue = "EIssueePrefix" };
        attrs["LEI"] = new RecursiveValue { StringValue = "EE111Corp" };
        acdc["a"] = new RecursiveValue { Dictionary = attrs };

        var edges = new RecursiveDictionary();
        edges["d"] = new RecursiveValue { StringValue = "EEdgeSAID" };
        var qvi = new RecursiveDictionary();
        qvi["n"] = new RecursiveValue { StringValue = "EQviCredSAID" };
        edges["qvi"] = new RecursiveValue { Dictionary = qvi };
        acdc["e"] = new RecursiveValue { Dictionary = edges };

        var rules = new RecursiveDictionary();
        rules["d"] = new RecursiveValue { StringValue = "ERuleSAID" };
        var usage = new RecursiveDictionary();
        usage["l"] = new RecursiveValue { StringValue = "Usage disclaimer text" };
        rules["usageDisclaimer"] = new RecursiveValue { Dictionary = usage };
        acdc["r"] = new RecursiveValue { Dictionary = rules };

        return acdc;
    }

    [Fact]
    public void ElideSingleSection_ReplacesWithSaid() {
        var acdc = BuildSampleAcdc();
        var elisionMap = new Dictionary<string, bool> { ["a"] = false, ["e"] = true, ["r"] = true };

        var result = CredentialHelper.ElideAcdc(acdc, elisionMap);

        Assert.Equal("EAttrSAID", result["a"].StringValue);
        Assert.NotNull(result["e"].Dictionary);
        Assert.NotNull(result["r"].Dictionary);
        Assert.Equal("", result["d"].StringValue);
    }

    [Fact]
    public void ElideMultipleSections_ReplacesAll() {
        var acdc = BuildSampleAcdc();
        var elisionMap = new Dictionary<string, bool> { ["a"] = false, ["e"] = false, ["r"] = false };

        var result = CredentialHelper.ElideAcdc(acdc, elisionMap);

        Assert.Equal("EAttrSAID", result["a"].StringValue);
        Assert.Equal("EEdgeSAID", result["e"].StringValue);
        Assert.Equal("ERuleSAID", result["r"].StringValue);
        Assert.Equal("", result["d"].StringValue);
    }

    [Fact]
    public void FullDisclosure_PreservesAllSections() {
        var acdc = BuildSampleAcdc();
        var elisionMap = new Dictionary<string, bool> { ["a"] = true, ["e"] = true, ["r"] = true };

        var result = CredentialHelper.ElideAcdc(acdc, elisionMap);

        Assert.NotNull(result["a"].Dictionary);
        Assert.NotNull(result["e"].Dictionary);
        Assert.NotNull(result["r"].Dictionary);
        // Top-level d is still reset to placeholder
        Assert.Equal("", result["d"].StringValue);
    }

    [Fact]
    public void MissingKeyInElisionMap_Ignored() {
        var acdc = BuildSampleAcdc();
        var elisionMap = new Dictionary<string, bool> { ["x"] = false };

        var result = CredentialHelper.ElideAcdc(acdc, elisionMap);

        // All original sections preserved
        Assert.NotNull(result["a"].Dictionary);
        Assert.NotNull(result["e"].Dictionary);
        Assert.NotNull(result["r"].Dictionary);
    }

    [Fact]
    public void SectionWithoutDField_Ignored() {
        var acdc = new RecursiveDictionary();
        acdc["d"] = new RecursiveValue { StringValue = "ETopSAID" };
        var noDSection = new RecursiveDictionary();
        noDSection["foo"] = new RecursiveValue { StringValue = "bar" };
        acdc["a"] = new RecursiveValue { Dictionary = noDSection };

        var elisionMap = new Dictionary<string, bool> { ["a"] = false };

        var result = CredentialHelper.ElideAcdc(acdc, elisionMap);

        // Section kept as dictionary since it has no "d" field to elide to
        Assert.NotNull(result["a"].Dictionary);
    }

    [Fact]
    public void ElideDoesNotMutateOriginal() {
        var acdc = BuildSampleAcdc();
        var elisionMap = new Dictionary<string, bool> { ["a"] = false };

        var result = CredentialHelper.ElideAcdc(acdc, elisionMap);

        // Original still has dictionary for "a"
        Assert.NotNull(acdc["a"].Dictionary);
        Assert.Equal("EOriginalTopLevelSAID", acdc["d"].StringValue);
        // Result has string for "a"
        Assert.Equal("EAttrSAID", result["a"].StringValue);
    }
}
