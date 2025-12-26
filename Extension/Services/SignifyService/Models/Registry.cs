using System.Text.Json.Serialization;

namespace Extension.Services.SignifyService.Models {
    /// <summary>
    /// Represents a credential registry.
    /// Matches signify-ts Registry type.
    /// </summary>
    public record Registry(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("regk")] string Regk,
        [property: JsonPropertyName("pre")] string Pre,
        [property: JsonPropertyName("state")] RegistryState? State = null
    );

    /// <summary>
    /// State of a credential registry.
    /// </summary>
    public record RegistryState(
        [property: JsonPropertyName("vn")] List<int> Vn,
        [property: JsonPropertyName("i")] string I,
        [property: JsonPropertyName("s")] string S,
        [property: JsonPropertyName("d")] string D,
        [property: JsonPropertyName("ii")] string Ii,
        [property: JsonPropertyName("dt")] string Dt,
        [property: JsonPropertyName("et")] string Et,
        [property: JsonPropertyName("bt")] string Bt,
        [property: JsonPropertyName("b")] List<string> B,
        [property: JsonPropertyName("c")] List<string> C
    );

    /// <summary>
    /// Arguments for creating a new credential registry.
    /// </summary>
    public record CreateRegistryArgs(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("registryName")] string RegistryName,
        [property: JsonPropertyName("toad")] int? Toad = null,
        [property: JsonPropertyName("noBackers")] bool NoBackers = false,
        [property: JsonPropertyName("baks")] List<string>? Baks = null,
        [property: JsonPropertyName("nonce")] string? Nonce = null
    );
}
