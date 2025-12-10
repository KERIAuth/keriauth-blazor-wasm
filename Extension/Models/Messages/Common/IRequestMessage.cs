namespace Extension.Models.Messages.Common;

/// <summary>
/// Marker interface for all request messages that expect a response.
/// TResponse is the expected response type.
/// </summary>
/// <typeparam name="TResponse">The expected response type that implements IResponseMessage</typeparam>
public interface IRequestMessage<TResponse> where TResponse : IResponseMessage
{
    /// <summary>
    /// The message type identifier.
    /// </summary>
    string Type { get; }
}
