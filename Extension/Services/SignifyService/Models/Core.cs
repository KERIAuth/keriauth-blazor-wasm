using System.Text.Json.Serialization;

namespace Extension.Services.SignifyService.Models {
    public enum Serials {
        [JsonPropertyName("JSON")]
        JSON,
        [JsonPropertyName("CBOR")]
        CBOR,
        [JsonPropertyName("MGPK")]
        MGPK
    }

    public enum Ident {
        [JsonPropertyName("KERI")]
        KERI,
        [JsonPropertyName("ACDC")]
        ACDC
    }

    public record Version(
        [property: JsonPropertyName("major")] int Major,
        [property: JsonPropertyName("minor")] int Minor
    );

    public record Sizage(
        [property: JsonPropertyName("hs")] int Hs,
        [property: JsonPropertyName("ss")] int Ss,
        [property: JsonPropertyName("ls")] int? Ls = null,
        [property: JsonPropertyName("fs")] int? Fs = null
    );

    public record MatterArgs(
        [property: JsonPropertyName("raw")] byte[]? Raw = null,
        [property: JsonPropertyName("code")] string? Code = null,
        [property: JsonPropertyName("qb64b")] byte[]? Qb64b = null,
        [property: JsonPropertyName("qb64")] string? Qb64 = null,
        [property: JsonPropertyName("qb2")] byte[]? Qb2 = null,
        [property: JsonPropertyName("rize")] int? Rize = null
    );

    public record IndexerArgs(
        [property: JsonPropertyName("raw")] byte[]? Raw = null,
        [property: JsonPropertyName("code")] string? Code = null,
        [property: JsonPropertyName("index")] int? Index = null,
        [property: JsonPropertyName("ondex")] int? Ondex = null,
        [property: JsonPropertyName("qb64b")] byte[]? Qb64b = null,
        [property: JsonPropertyName("qb64")] string? Qb64 = null,
        [property: JsonPropertyName("qb2")] byte[]? Qb2 = null
    );

    public record Matter(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("size")] int Size,
        [property: JsonPropertyName("raw")] byte[] Raw,
        [property: JsonPropertyName("qb64")] string Qb64,
        [property: JsonPropertyName("qb64b")] byte[] Qb64b,
        [property: JsonPropertyName("transferable")] bool Transferable,
        [property: JsonPropertyName("digestive")] bool Digestive
    );

    public record Indexer(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("size")] int Size,
        [property: JsonPropertyName("raw")] byte[] Raw,
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("ondex")] int? Ondex,
        [property: JsonPropertyName("qb64")] string Qb64,
        [property: JsonPropertyName("qb64b")] byte[] Qb64b
    );

    public record Counter(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("count")] int Count,
        [property: JsonPropertyName("countB64")] string CountB64,
        [property: JsonPropertyName("qb64")] string Qb64,
        [property: JsonPropertyName("qb64b")] byte[] Qb64b,
        [property: JsonPropertyName("qb2")] byte[] Qb2
    );

    public record Seqner(
        [property: JsonPropertyName("sn")] string Sn,
        [property: JsonPropertyName("snh")] string Snh,
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("size")] int Size,
        [property: JsonPropertyName("raw")] byte[] Raw,
        [property: JsonPropertyName("qb64")] string Qb64,
        [property: JsonPropertyName("qb64b")] byte[] Qb64b,
        [property: JsonPropertyName("qb2")] byte[] Qb2
    );
}
