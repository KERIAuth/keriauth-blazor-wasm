using System.Text.Json.Serialization;
using Extension.Models.Messages.Common;

namespace Extension.Models.Messages.AppBw.Responses;

/// <summary>
/// Response from BackgroundWorker for RequestAddIdentifier.
/// Contains success/failure status and optional error message.
/// </summary>
public record AddIdentifierResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("error")] string? Error = null,
    [property: JsonPropertyName("prefix")] string? Prefix = null
) : IResponseMessage
{
    /// <summary>
    /// Implements IResponseMessage.ErrorMessage by delegating to the Error property.
    /// </summary>
    public string? ErrorMessage => Error;
}
