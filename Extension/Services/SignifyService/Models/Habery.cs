using System.Text.Json.Serialization;

namespace Extension.Services.SignifyService.Models {
    public record HaberyArgs(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("passcode")] string? Passcode = null,
        [property: JsonPropertyName("seed")] string? Seed = null,
        [property: JsonPropertyName("aeid")] string? Aeid = null,
        [property: JsonPropertyName("pidx")] int? Pidx = null,
        [property: JsonPropertyName("salt")] string? Salt = null,
        [property: JsonPropertyName("tier")] string? Tier = null
    );

    public record MakeHabArgs(
        [property: JsonPropertyName("code")] string? Code = null,
        [property: JsonPropertyName("transferable")] bool? Transferable = null,
        [property: JsonPropertyName("isith")] [property: JsonConverter(typeof(NullableThresholdValueConverter))] ThresholdValue? Isith = null,
        [property: JsonPropertyName("icount")] int? Icount = null,
        [property: JsonPropertyName("nsith")] [property: JsonConverter(typeof(NullableThresholdValueConverter))] ThresholdValue? Nsith = null,
        [property: JsonPropertyName("ncount")] int? Ncount = null,
        [property: JsonPropertyName("toad")] [property: JsonConverter(typeof(NullableThresholdValueConverter))] ThresholdValue? Toad = null,
        [property: JsonPropertyName("wits")] List<string>? Wits = null,
        [property: JsonPropertyName("delpre")] string? Delpre = null,
        [property: JsonPropertyName("estOnly")] bool? EstOnly = null,
        [property: JsonPropertyName("DnD")] bool? DnD = null,
        [property: JsonPropertyName("data")] object? Data = null
    );

    public record Hab(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("prefix")] string Prefix,
        [property: JsonPropertyName("serder")] Serder? Serder = null,
        [property: JsonPropertyName("state")] HabState? State = null,
        [property: JsonPropertyName("transferable")] bool Transferable = true,
        [property: JsonPropertyName("windexes")] List<int>? Windexes = null
    );

    public record HabState(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("prefix")] string Prefix,
        [property: JsonPropertyName("transferable")] bool Transferable,
        [property: JsonPropertyName("state")] KeriState State,
        [property: JsonPropertyName("windexes")] List<int> Windexes,
        [property: JsonPropertyName("icp_dt")] string IcpDt
    );

    public record KeriState(
        [property: JsonPropertyName("vn")] List<int> Vn,
        [property: JsonPropertyName("i")] string I,
        [property: JsonPropertyName("s")] string S,
        [property: JsonPropertyName("p")] string? P,
        [property: JsonPropertyName("d")] string D,
        [property: JsonPropertyName("f")] string F,
        [property: JsonPropertyName("dt")] string Dt,
        [property: JsonPropertyName("et")] string Et,
        [property: JsonPropertyName("kt")] [property: JsonConverter(typeof(ThresholdValueConverter))] ThresholdValue Kt,
        [property: JsonPropertyName("k")] List<string> K,
        [property: JsonPropertyName("nt")] [property: JsonConverter(typeof(ThresholdValueConverter))] ThresholdValue Nt,
        [property: JsonPropertyName("n")] List<string> N,
        [property: JsonPropertyName("bt")] string Bt,
        [property: JsonPropertyName("b")] List<string> B,
        [property: JsonPropertyName("c")] List<string> C,
        [property: JsonPropertyName("ee")] EstablishmentState Ee,
        [property: JsonPropertyName("di")] string? Di = null
    );

    public record EstablishmentState(
        [property: JsonPropertyName("s")] string S,
        [property: JsonPropertyName("d")] string D,
        [property: JsonPropertyName("br")] List<string>? Br = null,
        [property: JsonPropertyName("ba")] List<string>? Ba = null
    );

    public record RotateArgs(
        [property: JsonPropertyName("transferable")] bool? Transferable = null,
        [property: JsonPropertyName("nsith")] [property: JsonConverter(typeof(NullableThresholdValueConverter))] ThresholdValue? Nsith = null,
        [property: JsonPropertyName("ncount")] int? Ncount = null,
        [property: JsonPropertyName("toad")] [property: JsonConverter(typeof(NullableThresholdValueConverter))] ThresholdValue? Toad = null,
        [property: JsonPropertyName("cuts")] List<string>? Cuts = null,
        [property: JsonPropertyName("adds")] List<string>? Adds = null,
        [property: JsonPropertyName("data")] object? Data = null
    );

    public record InteractArgs(
        [property: JsonPropertyName("data")] object? Data = null
    );

    public record DelegateArgs(
        [property: JsonPropertyName("delpre")] string Delpre,
        [property: JsonPropertyName("estOnly")] bool? EstOnly = null,
        [property: JsonPropertyName("data")] object? Data = null
    );
}
