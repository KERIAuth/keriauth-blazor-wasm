using System.Text.Json.Serialization;

namespace Extension.Services.SignifyService.Models {
    public record Verfer(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("size")] int Size,
        [property: JsonPropertyName("raw")] byte[] Raw,
        [property: JsonPropertyName("qb64")] string Qb64,
        [property: JsonPropertyName("qb64b")] byte[] Qb64b,
        [property: JsonPropertyName("transferable")] bool Transferable,
        [property: JsonPropertyName("digestive")] bool Digestive
    );

    public record Prefixer(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("size")] int Size,
        [property: JsonPropertyName("raw")] byte[] Raw,
        [property: JsonPropertyName("qb64")] string Qb64,
        [property: JsonPropertyName("qb64b")] byte[] Qb64b,
        [property: JsonPropertyName("transferable")] bool Transferable,
        [property: JsonPropertyName("digestive")] bool Digestive
    );

    public record Siger(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("size")] int Size,
        [property: JsonPropertyName("raw")] byte[] Raw,
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("ondex")] int? Ondex,
        [property: JsonPropertyName("qb64")] string Qb64,
        [property: JsonPropertyName("qb64b")] byte[] Qb64b,
        [property: JsonPropertyName("verfer")] Verfer? Verfer = null
    );

    public record Diger(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("size")] int Size,
        [property: JsonPropertyName("raw")] byte[] Raw,
        [property: JsonPropertyName("qb64")] string Qb64,
        [property: JsonPropertyName("qb64b")] byte[] Qb64b
    );

    public record Saider(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("size")] int Size,
        [property: JsonPropertyName("raw")] byte[] Raw,
        [property: JsonPropertyName("qb64")] string Qb64,
        [property: JsonPropertyName("qb64b")] byte[] Qb64b,
        [property: JsonPropertyName("kind")] string? Kind = null,
        [property: JsonPropertyName("label")] string? Label = null
    );

    public record Signer(
        [property: JsonPropertyName("raw")] byte[] Raw,
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("verfer")] Verfer Verfer,
        [property: JsonPropertyName("qb64")] string Qb64,
        [property: JsonPropertyName("qb64b")] byte[] Qb64b,
        [property: JsonPropertyName("transferable")] bool Transferable
    );

    public record Salter(
        [property: JsonPropertyName("raw")] byte[] Raw,
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("tier")] string Tier,
        [property: JsonPropertyName("qb64")] string Qb64,
        [property: JsonPropertyName("qb64b")] byte[] Qb64b
    );

    public record Cipher(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("raw")] byte[] Raw,
        [property: JsonPropertyName("qb64")] string Qb64,
        [property: JsonPropertyName("qb64b")] byte[] Qb64b
    );

    public record Encrypter(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("raw")] byte[] Raw,
        [property: JsonPropertyName("qb64")] string Qb64,
        [property: JsonPropertyName("qb64b")] byte[] Qb64b
    );

    public record Decrypter(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("raw")] byte[] Raw,
        [property: JsonPropertyName("qb64")] string Qb64,
        [property: JsonPropertyName("qb64b")] byte[] Qb64b,
        [property: JsonPropertyName("transferable")] bool Transferable
    );

    public record Tholder(
        [property: JsonPropertyName("thold")] object Thold,
        [property: JsonPropertyName("size")] int? Size = null,
        [property: JsonPropertyName("number")] int? Number = null,
        [property: JsonPropertyName("satisfy")] bool Satisfy = false,
        [property: JsonPropertyName("limen")] string? Limen = null
    );
}
