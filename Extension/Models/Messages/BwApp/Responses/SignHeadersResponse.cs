using System.Text.Json.Serialization;
using Extension.Models.Messages.Common;

namespace Extension.Models.Messages.BwApp.Responses;

/// <summary>
/// Response from App to BackgroundWorker for SignHeaders request.
/// Contains the signed headers if approved.
/// </summary>
public record SignHeadersResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("signedHeaders")] Dictionary<string, string>? SignedHeaders = null,
    [property: JsonPropertyName("prefix")] string? Prefix = null,
    [property: JsonPropertyName("error")] string? Error = null
) : IResponseMessage {
    /// <summary>
    /// Implements IResponseMessage.ErrorMessage by delegating to the Error property.
    /// </summary>
    public string? ErrorMessage => Error;

    /// <summary>
    /// Creates a successful response with the signed headers.
    /// </summary>
    public static SignHeadersResponse Approved(Dictionary<string, string> signedHeaders, string prefix) =>
        new(Success: true, SignedHeaders: signedHeaders, Prefix: prefix);

    /// <summary>
    /// Creates a failed response indicating the user canceled.
    /// </summary>
    public static SignHeadersResponse Canceled() =>
        new(Success: false, Error: "User canceled");

    /// <summary>
    /// Creates a failed response with an error message.
    /// </summary>
    public static SignHeadersResponse Failed(string error) =>
        new(Success: false, Error: error);
}
