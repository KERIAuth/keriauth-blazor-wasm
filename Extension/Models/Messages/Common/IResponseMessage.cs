namespace Extension.Models.Messages.Common;

/// <summary>
/// Interface for all response messages returned from request/response messaging.
/// Provides a consistent contract for success/failure indication.
/// </summary>
public interface IResponseMessage
{
    /// <summary>
    /// Indicates whether the request was successful.
    /// </summary>
    bool Success { get; }

    /// <summary>
    /// Error message if the request failed, null otherwise.
    /// </summary>
    string? ErrorMessage { get; }
}
