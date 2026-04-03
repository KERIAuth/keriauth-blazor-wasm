namespace Extension.Models.Messages.AppBw;

using System.Text.Json.Serialization;

public record ConfigureRequestPayload(
    [property: JsonPropertyName("adminUrl")] string AdminUrl,
    [property: JsonPropertyName("bootUrl")] string? BootUrl,
    [property: JsonPropertyName("bootAuthUsername")] string? BootAuthUsername,
    [property: JsonPropertyName("bootAuthPassword")] string? BootAuthPassword,
    [property: JsonPropertyName("passcode")] string Passcode,
    [property: JsonPropertyName("isNewAccount")] bool IsNewAccount,
    [property: JsonPropertyName("providerName")] string? ProviderName
);

public record ConfigureResponsePayload(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("error")] string? Error = null
);
