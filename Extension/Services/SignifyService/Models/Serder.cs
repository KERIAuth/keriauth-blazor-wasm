using System.Collections.Specialized;
using System.Text.Json.Serialization;

namespace Extension.Services.SignifyService.Models {
    public record Serder(
        [property: JsonPropertyName("kind")] Serials Kind,
        [property: JsonPropertyName("raw")] string Raw,
        [property: JsonPropertyName("ked")] OrderedDictionary Ked,
        [property: JsonPropertyName("ident")] Ident Ident,
        [property: JsonPropertyName("size")] int Size,
        [property: JsonPropertyName("version")] Version Version,
        [property: JsonPropertyName("pre")] string Pre,
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("sn")] int Sn,
        [property: JsonPropertyName("verfers")] List<Verfer> Verfers,
        [property: JsonPropertyName("digers")] List<Diger> Digers,
        [property: JsonPropertyName("said")] string? Said = null,
        [property: JsonPropertyName("pretty")] string? Pretty = null
    );

    public record EventMessage(
        [property: JsonPropertyName("v")] string V,
        [property: JsonPropertyName("t")] string T,
        [property: JsonPropertyName("d")] string D,
        [property: JsonPropertyName("i")] string I,
        [property: JsonPropertyName("s")] string S,
        [property: JsonPropertyName("p")] string? P = null,
        [property: JsonPropertyName("kt")] object Kt = null!,
        [property: JsonPropertyName("k")] List<string> K = null!,
        [property: JsonPropertyName("nt")] object? Nt = null,
        [property: JsonPropertyName("n")] List<string>? N = null,
        [property: JsonPropertyName("bt")] string? Bt = null,
        [property: JsonPropertyName("b")] List<string>? B = null,
        [property: JsonPropertyName("c")] List<string>? C = null,
        [property: JsonPropertyName("a")] List<object>? A = null,
        [property: JsonPropertyName("di")] string? Di = null,
        [property: JsonPropertyName("da")] OrderedDictionary? Da = null,
        [property: JsonPropertyName("r")] string? R = null,
        [property: JsonPropertyName("q")] OrderedDictionary? Q = null,
        [property: JsonPropertyName("e")] OrderedDictionary? E = null,
        [property: JsonPropertyName("dt")] string? Dt = null
    );

    public record RotationEvent(
        [property: JsonPropertyName("v")] string V,
        [property: JsonPropertyName("t")] string T,
        [property: JsonPropertyName("d")] string D,
        [property: JsonPropertyName("i")] string I,
        [property: JsonPropertyName("s")] string S,
        [property: JsonPropertyName("p")] string P,
        [property: JsonPropertyName("kt")] object Kt,
        [property: JsonPropertyName("k")] List<string> K,
        [property: JsonPropertyName("nt")] object Nt,
        [property: JsonPropertyName("n")] List<string> N,
        [property: JsonPropertyName("bt")] string Bt,
        [property: JsonPropertyName("br")] List<string> Br,
        [property: JsonPropertyName("ba")] List<string> Ba,
        [property: JsonPropertyName("a")] List<object> A
    );

    public record InceptionEvent(
        [property: JsonPropertyName("v")] string V,
        [property: JsonPropertyName("t")] string T,
        [property: JsonPropertyName("d")] string D,
        [property: JsonPropertyName("i")] string I,
        [property: JsonPropertyName("s")] string S,
        [property: JsonPropertyName("kt")] object Kt,
        [property: JsonPropertyName("k")] List<string> K,
        [property: JsonPropertyName("nt")] object Nt,
        [property: JsonPropertyName("n")] List<string> N,
        [property: JsonPropertyName("bt")] string Bt,
        [property: JsonPropertyName("b")] List<string> B,
        [property: JsonPropertyName("c")] List<string> C,
        [property: JsonPropertyName("a")] List<object> A,
        [property: JsonPropertyName("di")] string? Di = null
    );

    public record InteractionEvent(
        [property: JsonPropertyName("v")] string V,
        [property: JsonPropertyName("t")] string T,
        [property: JsonPropertyName("d")] string D,
        [property: JsonPropertyName("i")] string I,
        [property: JsonPropertyName("s")] string S,
        [property: JsonPropertyName("p")] string P,
        [property: JsonPropertyName("a")] List<object> A
    );

    public record DelegatedInceptionEvent(
        [property: JsonPropertyName("v")] string V,
        [property: JsonPropertyName("t")] string T,
        [property: JsonPropertyName("d")] string D,
        [property: JsonPropertyName("i")] string I,
        [property: JsonPropertyName("s")] string S,
        [property: JsonPropertyName("kt")] object Kt,
        [property: JsonPropertyName("k")] List<string> K,
        [property: JsonPropertyName("nt")] object Nt,
        [property: JsonPropertyName("n")] List<string> N,
        [property: JsonPropertyName("bt")] string Bt,
        [property: JsonPropertyName("b")] List<string> B,
        [property: JsonPropertyName("c")] List<string> C,
        [property: JsonPropertyName("a")] List<object> A,
        [property: JsonPropertyName("di")] string Di,
        [property: JsonPropertyName("da")] OrderedDictionary Da
    );

    public record DelegatedRotationEvent(
        [property: JsonPropertyName("v")] string V,
        [property: JsonPropertyName("t")] string T,
        [property: JsonPropertyName("d")] string D,
        [property: JsonPropertyName("i")] string I,
        [property: JsonPropertyName("s")] string S,
        [property: JsonPropertyName("p")] string P,
        [property: JsonPropertyName("kt")] object Kt,
        [property: JsonPropertyName("k")] List<string> K,
        [property: JsonPropertyName("nt")] object Nt,
        [property: JsonPropertyName("n")] List<string> N,
        [property: JsonPropertyName("bt")] string Bt,
        [property: JsonPropertyName("br")] List<string> Br,
        [property: JsonPropertyName("ba")] List<string> Ba,
        [property: JsonPropertyName("a")] List<object> A,
        [property: JsonPropertyName("di")] string Di,
        [property: JsonPropertyName("da")] OrderedDictionary Da
    );
}
