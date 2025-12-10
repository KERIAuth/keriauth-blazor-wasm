using System.Text.Json.Serialization;

namespace Extension.Models.Messages.AppBw.Requests;

/// <summary>
/// Payload for requesting a new identifier to be created.
/// </summary>
public record RequestAddIdentifierPayload(
    [property: JsonPropertyName("alias")] string Alias
);

/// <summary>
/// Message requesting BackgroundWorker to create a new identifier.
/// </summary>
public record AppBwRequestAddIdentifierMessage : AppBwMessage<RequestAddIdentifierPayload>
{
    public AppBwRequestAddIdentifierMessage(string alias)
        : base(AppBwMessageType.RequestAddIdentifier, 0, null, null, new RequestAddIdentifierPayload(alias)) { }
}
