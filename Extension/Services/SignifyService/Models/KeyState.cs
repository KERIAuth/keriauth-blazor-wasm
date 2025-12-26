using System.Text.Json.Serialization;

namespace Extension.Services.SignifyService.Models {
    /// <summary>
    /// Represents the current key state of an identifier.
    /// Matches signify-ts KeyState interface.
    /// </summary>
    public record KeyState(
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
        [property: JsonPropertyName("nt")] [property: JsonConverter(typeof(NullableThresholdValueConverter))] ThresholdValue? Nt,
        [property: JsonPropertyName("n")] List<string> N,
        [property: JsonPropertyName("bt")] string Bt,
        [property: JsonPropertyName("b")] List<string> B,
        [property: JsonPropertyName("c")] List<string> C,
        [property: JsonPropertyName("ee")] EstablishmentState Ee,
        [property: JsonPropertyName("di")] string? Di = null
    );

    // Note: EstablishmentState is defined in Habery.cs
}
