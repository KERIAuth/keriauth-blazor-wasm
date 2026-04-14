using System.Text.Encodings.Web;
using System.Text.Json;
using Extension.Helper;

namespace Extension.Tests.Services;

// Proves that RecursiveDictionary's JSON round-trip is idempotent: parse -> minify -> parse -> minify
// must be byte-identical on the second pass. This is a prerequisite for SAID-safe canonical
// serialization but does not by itself prove our output matches signify-ts' canonical form.
//
// A prior length-match test that compared produced bytes against the `v` field's declared length was
// removed because the vLEI compact-*.json samples have internally inconsistent `v` fields
// (did:keri: prefixes on i/ri or `v` carried over from uncompacted issuance). Do not re-add that
// test without a better oracle — either a credential whose `v` is known to match the file, or a
// SAID-equality check against an authoritative source (signify-ts via JS interop, in-browser).
public class CesrRoundTripTests {
    private static readonly JsonSerializerOptions Opts = new() {
        Converters = { new RecursiveDictionaryConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private static string FixturePath(string filename) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Helper", filename));

    [Theory]
    [InlineData("compact-ecr-authorization-vlei-credential.json")]
    [InlineData("compact-legal-entity-engagement-context-role-vLEI-credential.json")]
    [InlineData("compact-legal-entity-official-organizational-role-vLEI-credential.json")]
    [InlineData("compact-legal-entity-vLEI-credential.json")]
    [InlineData("compact-oor-authorization-vlei-credential.json")]
    [InlineData("compact-qualified-vLEI-issuer-vLEI-credential.json")]
    public void CompactAcdc_SecondRoundTripIsByteIdentical(string filename) {
        var fileBytes = File.ReadAllBytes(FixturePath(filename));
        var rd = JsonSerializer.Deserialize<RecursiveDictionary>(fileBytes, Opts)!;
        var minified = JsonSerializer.SerializeToUtf8Bytes(rd, Opts);

        var rd2 = JsonSerializer.Deserialize<RecursiveDictionary>(minified, Opts)!;
        var minified2 = JsonSerializer.SerializeToUtf8Bytes(rd2, Opts);

        Assert.Equal(minified, minified2);
    }
}
