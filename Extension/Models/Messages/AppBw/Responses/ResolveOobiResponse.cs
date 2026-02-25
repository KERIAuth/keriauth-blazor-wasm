using System.Text.Json.Serialization;
using Extension.Models.Messages.Common;

namespace Extension.Models.Messages.AppBw.Responses;

public record ResolveOobiResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("error")] string? Error = null
) : IResponseMessage {
    public string? ErrorMessage => Error;
}
