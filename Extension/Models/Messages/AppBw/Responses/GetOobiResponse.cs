using System.Text.Json.Serialization;
using Extension.Models.Messages.Common;

namespace Extension.Models.Messages.AppBw.Responses;

public record GetOobiResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("error")] string? Error = null,
    [property: JsonPropertyName("oobi")] string? Oobi = null
) : IResponseMessage {
    public string? ErrorMessage => Error;
}
