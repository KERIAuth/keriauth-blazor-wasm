using System.Text.Json.Serialization;
using Extension.Models.Messages.Common;

namespace Extension.Models.Messages.BwApp.Responses;

/// <summary>
/// Response from App to BackgroundWorker for NotifyUserOfUpdate request.
/// Indicates user has acknowledged the update notification.
/// </summary>
public record NotifyUserOfUpdateResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("error")] string? Error = null
) : IResponseMessage {
    /// <summary>
    /// Implements IResponseMessage.ErrorMessage by delegating to the Error property.
    /// </summary>
    public string? ErrorMessage => Error;

    /// <summary>
    /// Creates a successful response indicating user acknowledged the update.
    /// </summary>
    public static NotifyUserOfUpdateResponse Acknowledged() =>
        new(Success: true);

    /// <summary>
    /// Creates a failed response with an error message.
    /// </summary>
    public static NotifyUserOfUpdateResponse Failed(string error) =>
        new(Success: false, Error: error);
}
