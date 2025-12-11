using System.Text.Json.Serialization;
using Extension.Models.Messages.Common;

namespace Extension.Models.Messages.BwApp.Responses;

/// <summary>
/// Response from App to BackgroundWorker for SelectAuthorize request.
/// Contains the selected identifier prefix if authorized.
/// </summary>
public record SelectAuthorizeResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("prefix")] string? Prefix = null,
    [property: JsonPropertyName("error")] string? Error = null
) : IResponseMessage {
    /// <summary>
    /// Implements IResponseMessage.ErrorMessage by delegating to the Error property.
    /// </summary>
    public string? ErrorMessage => Error;

    /// <summary>
    /// Creates a successful response with the selected identifier prefix.
    /// </summary>
    public static SelectAuthorizeResponse Authorized(string prefix) =>
        new(Success: true, Prefix: prefix);

    /// <summary>
    /// Creates a failed response indicating the user canceled.
    /// </summary>
    public static SelectAuthorizeResponse Canceled() =>
        new(Success: false, Error: "User canceled");

    /// <summary>
    /// Creates a failed response with an error message.
    /// </summary>
    public static SelectAuthorizeResponse Failed(string error) =>
        new(Success: false, Error: error);
}
